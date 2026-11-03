using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Someone (see <see cref="Party"/>) asks for the participants of the current group, or of the group with
/// the given id. The outcome is the scenario's last response; a list that was shown is remembered, so a scenario
/// can compare it with an earlier one.</summary>
public sealed class LookAtTheParticipants(string who, string? groupReservationId = null) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var groupId = groupReservationId ?? actor.Recall<GroupBooking>().GroupReservationId;
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi())
            .GetParticipantsAsync(groupId, Party.TokenOf(actor, who)));
        if (response.Succeeded)
            actor.Remember(response.Json<GroupSnapshot>());
    }
}
