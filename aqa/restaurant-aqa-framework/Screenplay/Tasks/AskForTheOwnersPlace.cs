using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Someone (see <see cref="Party"/>) asks for the owner's own place (guest session) in the group of the
/// current booking. The outcome is the scenario's last response; every place handed out is remembered.</summary>
public sealed class AskForTheOwnersPlace(string who = Party.Owner) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var booking = actor.Recall<GroupBooking>();
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi())
            .GetOwnerPlaceAsync(booking.ReservationId, Party.TokenOf(actor, who)));
        if (response.Succeeded)
            actor.Remember(response.Json<OwnerPlaceResponse>());
    }
}
