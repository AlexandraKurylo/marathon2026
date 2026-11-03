using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;
using Restaurant_Aqa_Framework.Api.Dtos.Locations;
using Restaurant_Aqa_Framework.Api.Seeding;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Registers a fresh account and adds it to the waiters of the scenario's location, so bookings there
/// are served by a waiter that belongs to this scenario only. The waiter signs in after being added, so their
/// token carries the waiter role. Removed from the waiters list again afterwards.</summary>
public sealed class HireAWaiter : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var location = actor.Recall<LocationResponse>();
        var person = SampleDataFactory.RegisterRequest();
        var auth = new AuthApi(actor.UsingTheApi());
        var waiters = new WaitersApi(actor.UsingTheApi());

        var registered = await auth.RegisterAsync(person);
        if (!registered.Ok)
            throw new InvalidOperationException(
                $"Registering the waiter failed with {registered.Status}: {await registered.ReadErrorMessageAsync()}");

        var added = await waiters.AddWaiterAsync(new WaiterAssignmentRequest(person.Email, location.Id));
        if (!added.Ok)
            throw new InvalidOperationException(
                $"Adding the waiter to the location failed with {added.Status}: {await added.ReadErrorMessageAsync()}");

        actor.Recall<CleanupScope>().TrackDelete(
            $"waiter:{person.Email}",
            () => waiters.RemoveWaiterAsync(new WaiterRemovalRequest(person.Email)));

        var token = await SignUpForTheParty.SignInAsync(auth, person.Email, person.Password, Party.Waiter);
        actor.Remember(Party.Waiter, new PartyAccount(person.Email, person.Password, token));
    }
}
