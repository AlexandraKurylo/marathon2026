using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Someone (see <see cref="Party"/>) removes a participant, by guest session id, from the current group.
/// The outcome is the scenario's last response; the updated list is remembered when the removal succeeded.</summary>
public sealed class RemoveTheParticipant(string guestSessionId, string who) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>();
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi())
            .RemoveParticipantAsync(booking.GroupReservationId, guestSessionId, Party.TokenOf(actor, who)));
        if (response.Succeeded)
            actor.Remember(response.Json<GroupSnapshot>());
    }
}
