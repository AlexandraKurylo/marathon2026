using System.Globalization;
using System.Text.Json;
using Restaurant_Aqa_Framework.Api;

namespace Restaurant_Aqa_Framework.Screenplay.Questions;

/// <summary>The pre-order of the current booking as the scenario's waiter sees it.</summary>
public sealed record WaiterOrderView(bool Found, string Status, IReadOnlyDictionary<string, int> PortionsByDishId, string Body);

public sealed class TheOrderTheWaiterSees : IQuestion<WaiterOrderView>
{
    private static readonly string[] StatusNames = ["Created", "Submitted", "Cancelled", "SubmittedByCustomer"];

    public async Task<WaiterOrderView> AnsweredByAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>();
        var waiter = actor.Recall<PartyAccount>(Party.Waiter);
        var response = await new OrdersApi(actor.UsingTheApi()).GetOrderAsync(booking.ReservationId, waiter.IdToken);
        var body = await response.TextAsync();
        if (!response.Ok)
            return new WaiterOrderView(false, "", new Dictionary<string, int>(), body);

        using var json = JsonDocument.Parse(body);
        var root = json.RootElement;

        var status = root.GetProperty("status");
        var statusName = status.ValueKind == JsonValueKind.Number && status.GetInt32() is var index && index >= 0 && index < StatusNames.Length
            ? StatusNames[index]
            : status.ValueKind == JsonValueKind.Number ? status.GetInt32().ToString(CultureInfo.InvariantCulture) : status.GetString() ?? "";

        var portions = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var item in root.GetProperty("orderItems").EnumerateArray())
        {
            var dishId = item.GetProperty("dish").GetProperty("id").GetString() ?? "";
            portions[dishId] = portions.GetValueOrDefault(dishId) + item.GetProperty("quantity").GetInt32();
        }

        return new WaiterOrderView(true, statusName, portions, body);
    }
}
