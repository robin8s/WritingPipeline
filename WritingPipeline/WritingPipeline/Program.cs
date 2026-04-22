using LlmTornado;
using LlmTornado.Agents;
using LlmTornado.Chat;
using LlmTornado.Chat.Models;
using LlmTornado.Code;

public enum ReviewStatus
{
    Unknown,
    Ready,
    Revise
}

public class EditorialReview
{
    public ReviewStatus Status { get; set; } = ReviewStatus.Unknown;
    public string Rationale { get; set; } = "";

    public List<string> RevisionTasks { get; set; } = new();
}

class Program
{
    static async Task Main()
    {
        TornadoApi api = new TornadoApi(
            new Uri("http://127.0.0.1:1234"),
            string.Empty,
            LLmProviders.OpenAi);

        TornadoAgent writer = CreateWriterAgent(api);
        TornadoAgent editor = CreateEditorAgent(api);

        while (true)
        {
            Console.Write("Welcome to the Writing Pipeline. Enter a writing task.\nCOMMANDS: (/exit to quit)  (/clear to clear screen)\n");

            string? task = Console.ReadLine();

            if (string.IsNullOrWhiteSpace(task))
                continue;

            if (task.Equals("/exit", StringComparison.OrdinalIgnoreCase))
                break;

            // /clear command 
            if (task.Equals("/clear", StringComparison.OrdinalIgnoreCase))
            {
                Console.Clear();
                continue;
            }

            string draft = await GenerateDraftAsync(writer, task);

            Console.WriteLine();

            //cyan color for DRAFT
            Console.ForegroundColor = ConsoleColor.Cyan;
            Console.WriteLine("=== DRAFT ===");
            Console.ResetColor();

            Console.WriteLine(draft);
            Console.WriteLine();

            EditorialReview review = await ReviewDraftAsync(editor, task, draft);

            // yellow color for EDITOR REVIEW
            Console.ForegroundColor = ConsoleColor.Yellow;
            Console.WriteLine("=== EDITOR REVIEW ===");
            Console.ResetColor();

            Console.WriteLine($"STATUS: {review.Status}");
            Console.WriteLine($"RATIONALE: {review.Rationale}");

            if (review.RevisionTasks.Count > 0)
            {
                // darkyellow color for REVISION TASKS
                Console.ForegroundColor = ConsoleColor.DarkYellow;
                Console.WriteLine("REVISION TASKS:");
                Console.ResetColor();

                foreach (string item in review.RevisionTasks)
                {
                    Console.WriteLine($"- {item}");
                }
            }

            Console.WriteLine();

            // TODO 4
            int rounds = 0;

            while (review.Status == ReviewStatus.Revise && rounds < 3)
            {
                // magenta color for REVISION ROUND
                Console.ForegroundColor = ConsoleColor.Magenta;
                Console.WriteLine($"REVISION ROUND {rounds}");
                Console.ResetColor();

                Conversation convo = await writer.Run(
                    input: $"""
            Improve this draft based on the feedback.

            TASK:
            {task}

            DRAFT:
            {draft}

            FEEDBACK:
            {review.Rationale}

            TASKS:
            {string.Join("\n", review.RevisionTasks)}
            """
                );

                draft = GetLastAssistantText(convo);

                // green color for REVISED DRAFT
                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("=== REVISED DRAFT ===");
                Console.ResetColor();

                Console.WriteLine(draft);
                Console.WriteLine();

                review = await ReviewDraftAsync(editor, task, draft);

                // yellow for EDITOR REVIEW (loop)
                Console.ForegroundColor = ConsoleColor.Yellow;
                Console.WriteLine("=== EDITOR REVIEW ===");
                Console.ResetColor();

                Console.WriteLine($"STATUS: {review.Status}");
                Console.WriteLine($"RATIONALE: {review.Rationale}");

                if (review.RevisionTasks.Count > 0)
                {
                    // piss yellow color for REVISION TASKS
                    Console.ForegroundColor = ConsoleColor.DarkYellow;
                    Console.WriteLine("REVISION TASKS:");
                    Console.ResetColor();

                    foreach (string item in review.RevisionTasks)
                    {
                        Console.WriteLine($"- {item}");
                    }
                }

                Console.WriteLine();

                rounds++;
            }

            // blu
            Console.ForegroundColor = ConsoleColor.Blue;
            Console.WriteLine("=== FINAL DRAFT ===");
            Console.ResetColor();

            Console.WriteLine(draft);
        }
    }

    static TornadoAgent CreateWriterAgent(TornadoApi api)
    {
        return new TornadoAgent(
            client: api,
            model: new ChatModel("google/gemma-3-4b"),
            name: "Writer",
            tools: [],
            instructions: """
            You are a focused writing assistant.
            Produce a clear, well-structured draft that directly fulfills the given writing task.
            Use only information that is relevant to the task and maintain logical flow throughout.
            Do not include explanations, notes, or meta-commentary—return the draft only.
            """
        );
    }

    static TornadoAgent CreateEditorAgent(TornadoApi api)
    {
        return new TornadoAgent(
            client: api,
            model: new ChatModel("google/gemma-3-4b"),
            name: "Editor",
            tools: [],
            instructions: """
            You are an editor evaluating a draft for clarity, structure, relevance, and task completion.
            Make a strict decision: the draft is either READY or REVISE.
            Use the following output format exactly:
            STATUS: READY or REVISE
            RATIONALE: one concise sentence explaining the decision
            REVISION TASKS:
            - task one
            - task two
            If the draft is READY, still include the REVISION TASKS: header but do not include any bullet points.
            Be strict: mark REVISE whenever the draft is unclear, incomplete, unfocused, disorganized, or contains unnecessary content.
            """
        );
    }

    static async Task<string> GenerateDraftAsync(TornadoAgent writer, string task)
    {
        Conversation conversation = await writer.Run(
            input: $"""
                    Write a draft for this task:

                    {task}
                    """
        );

        return GetLastAssistantText(conversation);
    }

    static async Task<EditorialReview> ReviewDraftAsync(TornadoAgent editor, string task, string draft)
    {
        Conversation conversation = await editor.Run(
            input: $"""
                    Review this draft against the original task.

                    ORIGINAL TASK:
                    {task}

                    DRAFT:
                    {draft}
                    """
        );

        string editorResponse = GetLastAssistantText(conversation);
        return ParseReview(editorResponse);
    }

    static EditorialReview ParseReview(string editorResponse)
    {
        EditorialReview review = new EditorialReview();

        if (string.IsNullOrWhiteSpace(editorResponse))
        {
            review.Rationale = "No response from editor.";
            return review;
        }

        string[] lines = editorResponse
            .Split("\n", StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);

        bool readingTasks = false;

        foreach (string line in lines)
        {
            string trimmedLine = line.Trim();

            if (trimmedLine.StartsWith("STATUS:", StringComparison.OrdinalIgnoreCase))
            {
                string statusText = trimmedLine.Substring("STATUS:".Length).Trim();
                review.Status = Enum.TryParse<ReviewStatus>(statusText, true, out var s) ? s : ReviewStatus.Unknown;
                readingTasks = false;
            }
            else if (trimmedLine.StartsWith("RATIONALE:", StringComparison.OrdinalIgnoreCase))
            {
                string rationaleText = trimmedLine.Substring("RATIONALE:".Length).Trim();
                int idx = Array.IndexOf(lines, line);
                if (string.IsNullOrWhiteSpace(rationaleText) && idx + 1 < lines.Length)
                    rationaleText = lines[idx + 1].Trim();
                review.Rationale = string.IsNullOrWhiteSpace(rationaleText) ? "No rationale was provided." : rationaleText;
                readingTasks = false;
            }
            else if (trimmedLine.StartsWith("REVISION TASKS:", StringComparison.OrdinalIgnoreCase))
            {
                readingTasks = true;
            }
            else if (readingTasks)
            {
                string taskText = trimmedLine.StartsWith("-") ? trimmedLine.Substring(1).Trim() : trimmedLine;
                if (!string.IsNullOrWhiteSpace(taskText))
                    review.RevisionTasks.Add(taskText);
            }
        }

        if (string.IsNullOrWhiteSpace(review.Rationale))
            review.Rationale = "No rationale was provided.";

        return review;
    }

    static string GetLastAssistantText(Conversation conversation)
    {
        ChatMessage lastMessage = conversation.Messages.Last();

        if (!string.IsNullOrWhiteSpace(lastMessage.Content))
            return lastMessage.Content.Trim();

        if (lastMessage.Parts is not null)
        {
            string combined = string.Join(
                "\n",
                lastMessage.Parts
                    .Select(p => p.Text)
                    .Where(t => !string.IsNullOrWhiteSpace(t)));

            if (!string.IsNullOrWhiteSpace(combined))
                return combined.Trim();
        }

        return "";
    }
}