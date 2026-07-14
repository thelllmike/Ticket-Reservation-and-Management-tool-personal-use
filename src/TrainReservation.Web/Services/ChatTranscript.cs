using TrainReservation.Web.Models;

namespace TrainReservation.Web.Services;

/// <summary>
/// The running chat conversation.
///
/// Held in a singleton rather than in session state: this is a single-user personal tool with no
/// authentication, and the rest of the application already keeps its state in memory for the life
/// of the process. Adding session middleware would buy nothing here.
/// </summary>
public class ChatTranscript
{
    private readonly List<ChatMessage> _messages = new();
    private readonly object _sync = new();

    public IReadOnlyList<ChatMessage> Messages
    {
        get { lock (_sync) return _messages.ToList(); }
    }

    public void Add(ChatMessage message)
    {
        lock (_sync) _messages.Add(message);
    }

    public void Clear()
    {
        lock (_sync) _messages.Clear();
    }
}
