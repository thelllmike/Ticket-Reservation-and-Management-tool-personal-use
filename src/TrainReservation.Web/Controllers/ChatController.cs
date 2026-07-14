using Microsoft.AspNetCore.Mvc;
using TrainReservation.Services;
using TrainReservation.Web.Models;
using TrainReservation.Web.Services;

namespace TrainReservation.Web.Controllers;

/// <summary>
/// The prediction chatbot. Every answer is computed locally from the seeded booking history —
/// there is no model and no network call anywhere behind this page.
/// </summary>
public class ChatController : Controller
{
    private readonly IPredictionService _predictions;
    private readonly IScheduleService _schedule;
    private readonly ChatTranscript _transcript;

    public ChatController(IPredictionService predictions, IScheduleService schedule, ChatTranscript transcript)
    {
        _predictions = predictions;
        _schedule = schedule;
        _transcript = transcript;
    }

    public IActionResult Index() => View(BuildModel());

    /// <summary>Answers a typed question.</summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Ask(string question)
    {
        if (string.IsNullOrWhiteSpace(question))
            return RedirectToAction(nameof(Index));

        _transcript.Add(new ChatMessage { Text = question, FromUser = true });

        var response = _predictions.Ask(question);

        _transcript.Add(new ChatMessage
        {
            Text = response.Message,
            FromUser = false,
            Prediction = response.Prediction
        });

        return RedirectToAction(nameof(Index));
    }

    /// <summary>
    /// Answers from the dropdowns instead of free text. This is the escape hatch when the bot could
    /// not tell what the question meant and asked the user to disambiguate.
    /// </summary>
    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Predict(int routeId, DateOnly date)
    {
        var route = _schedule.GetRoute(routeId);

        if (route is null)
        {
            TempData["Error"] = "Choose a route first.";
            return RedirectToAction(nameof(Index));
        }

        _transcript.Add(new ChatMessage
        {
            Text = $"{route.Description} on {date:dddd d MMMM}",
            FromUser = true
        });

        var response = _predictions.Predict(routeId, date);

        _transcript.Add(new ChatMessage
        {
            Text = response.Message,
            FromUser = false,
            Prediction = response.Prediction
        });

        return RedirectToAction(nameof(Index));
    }

    [HttpPost]
    [ValidateAntiForgeryToken]
    public IActionResult Clear()
    {
        _transcript.Clear();
        return RedirectToAction(nameof(Index));
    }

    private ChatViewModel BuildModel() => new()
    {
        Transcript = _transcript.Messages.ToList(),
        Routes = _schedule.GetRoutes().ToList(),
        Date = DateOnly.FromDateTime(DateTime.Today).AddDays(7)
    };
}
