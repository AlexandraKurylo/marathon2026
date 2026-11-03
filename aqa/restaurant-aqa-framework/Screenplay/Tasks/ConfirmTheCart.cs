using Restaurant_Aqa_Framework.Api;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>A guest confirms their cart, so it counts when the owner confirms the group pre-order.</summary>
public sealed class ConfirmTheCart(string guestName) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var guest = actor.Recall<JoinedGuest>(guestName);
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi())
            .SubmitCartAsync(guest.SessionId, guest.SessionToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"{guestName} could not confirm the cart: {response.Status} {response.Body}");
    }
}
