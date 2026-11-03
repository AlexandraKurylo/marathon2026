using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>A guest follows the invitation of a booking (the current one, or the named one) and joins its group.
/// The outcome is the scenario's last response; a guest who got in is remembered under their name.</summary>
public sealed class JoinTheGroup(string guestName, string? booking = null, bool mustSucceed = true) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var group = actor.Recall<GroupBooking>(booking);
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi())
            .JoinAsync(new JoinGroupRequest(group.GroupReservationId, guestName)));
        if (!response.Succeeded)
        {
            if (mustSucceed)
                throw new InvalidOperationException($"{guestName} could not join the group: {response.Status} {response.Body}");

            return;
        }

        var session = response.Json<JoinGroupResponse>();
        actor.Remember(guestName, new JoinedGuest(guestName, session.SessionId, session.SessionToken));
    }
}
