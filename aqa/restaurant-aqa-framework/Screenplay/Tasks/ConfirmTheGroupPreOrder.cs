using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>What the restaurant received when the owner confirmed the group pre-order.</summary>
public sealed record ConfirmedPreOrder(IReadOnlyList<ConfirmedDish> Dishes);

/// <summary>Someone (see <see cref="Party"/>) confirms the consolidated pre-order of the current group, or of the
/// group with the given id. The outcome is the scenario's last response.</summary>
public sealed class ConfirmTheGroupPreOrder(string who, string? groupReservationId = null) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>();
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi()).ConfirmGroupAsync(
            groupReservationId ?? booking.GroupReservationId,
            new ConfirmGroupRequest(booking.ReservationId),
            Party.TokenOf(actor, who)));
        if (response.Succeeded)
            actor.Remember(new ConfirmedPreOrder(response.Json<List<ConfirmedDish>>()));
    }
}
