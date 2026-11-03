using Restaurant_Aqa_Framework.Api.Dtos.Dishes;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;
using Restaurant_Aqa_Framework.Screenplay;
using Restaurant_Aqa_Framework.Screenplay.Questions;
using Restaurant_Aqa_Framework.Screenplay.Tasks;

namespace Restaurant_Aqa_Framework.StepDefinitions;

/// <summary>Group pre-order: the owner of a booking, the guests who follow the invitation, other customers, the
/// waiter of the location and visitors who are not signed in. Who does what is named in the feature file
/// ("the owner", "another customer", "the waiter", "a visitor" or a guest's name) and mapped to credentials in
/// <see cref="Party"/>; how a refusal shows up on the wire is decided here only.</summary>
[Binding]
public sealed class GroupPreOrderStepDefinitions(Actor actor) : StepDefinitionBase(actor)
{
    private const string MissingId = "00000000-0000-0000-0000-000000000000";

    // ---- setting the scene ----

    [Given("a location with its own waiter, two tables and {int} dishes on the menu")]
    public async Task GivenALocationWithWaiterTablesAndDishes(int dishes)
    {
        await Actor.AttemptsToAsync(new SetUpLocation(), new AddTable(6), new AddTable(4));
        for (var i = 0; i < dishes; i++)
            await Actor.AttemptsToAsync(new AddDish(i % 3 == 2 ? DishType.Dessert : DishType.MainCourse));
        await Actor.AttemptsToAsync(new HireAWaiter());
    }

    [Given("another customer is signed in")]
    public Task GivenAnotherCustomer() => Actor.AttemptsToAsync(new SignUpForTheParty(Party.AnotherCustomer));

    [Given("the owner has booked the table for a group of {int}")]
    public Task GivenOwnerBookedTheTable(int guests) =>
        Actor.AttemptsToAsync(new SignUpForTheParty(Party.Owner), new BookATableForAGroup(guests));

    [Given("the owner has booked the table for a group of {int} without starting a pre-order")]
    public Task GivenOwnerBookedTheTableWithoutPreOrder(int guests) =>
        Actor.AttemptsToAsync(new SignUpForTheParty(Party.Owner), new BookATableForAGroup(guests, withPreOrder: false));

    [Given("the owner has also booked the second table for a group of {int}")]
    public Task GivenOwnerBookedTheSecondTable(int guests) =>
        Actor.AttemptsToAsync(new BookATableForAGroup(guests, tableNumber: 2, name: "second"));

    [Given("guest {string} has joined the group")]
    public Task GivenGuestJoined(string guest) => Actor.AttemptsToAsync(new JoinTheGroup(guest));

    [Given("guest {string} has joined the second group")]
    public Task GivenGuestJoinedTheSecondGroup(string guest) => Actor.AttemptsToAsync(new JoinTheGroup(guest, "second"));

    [Given("{string} has put {int} portion(s) of dish {int} in the cart")]
    [When("{string} puts {int} more portion(s) of dish {int} in the cart")]
    public Task GivenGuestPutDishInCart(string guest, int portions, int dish) =>
        Actor.AttemptsToAsync(new PutADishInTheCart(guest, dish, portions));

    [Given("{string} has confirmed the cart")]
    public Task GivenGuestConfirmedCart(string guest) => Actor.AttemptsToAsync(new ConfirmTheCart(guest));

    // ---- what people do ----

    [Given("the owner has looked at the participants")]
    [When("the owner looks at the participants")]
    [When("the owner looks at the participants again")]
    public Task WhenOwnerLooksAtParticipants() => Actor.AttemptsToAsync(new LookAtTheParticipants(Party.Owner));

    [When("{string} tries to look at the participants")]
    public Task WhenSomeoneTriesToLookAtParticipants(string who) => Actor.AttemptsToAsync(new LookAtTheParticipants(who));

    [When("the owner looks at the participants of a group that does not exist")]
    public Task WhenOwnerLooksAtMissingGroup() => Actor.AttemptsToAsync(new LookAtTheParticipants(Party.Owner, MissingId));

    [Given("the owner has confirmed the group pre-order")]
    [When("the owner confirms the group pre-order")]
    [When("the owner confirms the group pre-order again")]
    public Task WhenOwnerConfirms() => Actor.AttemptsToAsync(new ConfirmTheGroupPreOrder(Party.Owner));

    [When("{string} tries to confirm the group pre-order")]
    public Task WhenSomeoneTriesToConfirm(string who) => Actor.AttemptsToAsync(new ConfirmTheGroupPreOrder(who));

    [When("the owner confirms the pre-order of a group that does not exist")]
    public Task WhenOwnerConfirmsMissingGroup() => Actor.AttemptsToAsync(new ConfirmTheGroupPreOrder(Party.Owner, MissingId));

    [When("the owner removes {string} from the group")]
    public Task WhenOwnerRemoves(string guest) =>
        Actor.AttemptsToAsync(new RemoveTheParticipant(Recall<JoinedGuest>(guest).SessionId, Party.Owner));

    [When("{string} tries to remove {string} from the group")]
    public Task WhenSomeoneTriesToRemove(string who, string guest) =>
        Actor.AttemptsToAsync(new RemoveTheParticipant(Recall<JoinedGuest>(guest).SessionId, who));

    [When("the owner removes a participant who does not exist")]
    public Task WhenOwnerRemovesMissingParticipant() => Actor.AttemptsToAsync(new RemoveTheParticipant(MissingId, Party.Owner));

    [When("the owner asks for their own place in the group")]
    [When("the owner asks for their own place in the group again")]
    public Task WhenOwnerAsksForOwnPlace() => Actor.AttemptsToAsync(new AskForTheOwnersPlace());

    [When("{string} tries to get the owner's place in the group")]
    public Task WhenSomeoneAsksForOwnersPlace(string who) => Actor.AttemptsToAsync(new AskForTheOwnersPlace(who));

    [When("guest {string} tries to join the group")]
    public Task WhenGuestTriesToJoin(string guest) => Actor.AttemptsToAsync(new JoinTheGroup(guest, mustSucceed: false));

    // ---- what the owner sees ----

    [Then("the participants are:")]
    public async Task ThenParticipantsAre(DataTable expected)
    {
        var snapshot = await LatestSnapshotAsync();
        var guests = snapshot.Participants.Where(p => p.Type == "Guest").Select(p => p.Name).OrderBy(n => n, StringComparer.Ordinal).ToList();
        var wanted = expected.Rows.Select(r => r["name"]).OrderBy(n => n, StringComparer.Ordinal).ToList();

        await Assert.That(guests.SequenceEqual(wanted)).IsTrue()
            .Because($"listed guests were [{string.Join(", ", guests)}]");
    }

    [Then("no participants are listed")]
    public async Task ThenNoParticipants()
    {
        var snapshot = await LatestSnapshotAsync();
        await Assert.That(snapshot.Participants.Count).IsEqualTo(0)
            .Because($"listed: [{string.Join(", ", snapshot.Participants.Select(p => p.Name))}]");
    }

    [Then("no guests other than the owner are listed")]
    public async Task ThenOnlyTheOwnerIsListed()
    {
        var snapshot = await LatestSnapshotAsync();
        await Assert.That(snapshot.Participants.Any(p => p.Type == "Guest")).IsFalse()
            .Because($"listed: [{string.Join(", ", snapshot.Participants.Select(p => $"{p.Name} ({p.Type})"))}]");
    }

    [Then("{string} is not listed")]
    public async Task ThenGuestIsNotListed(string guest)
    {
        var snapshot = await LatestSnapshotAsync();
        await Assert.That(snapshot.Participants.Any(p => p.Name == guest)).IsFalse()
            .Because($"listed: [{string.Join(", ", snapshot.Participants.Select(p => p.Name))}]");
    }

    [Then("{string} has {int} portion(s) of dish {int}")]
    public async Task ThenGuestHasPortions(string guest, int portions, int dish)
    {
        var participant = await ParticipantAsync(guest);
        var dishId = DishNumber.Resolve(Actor, dish).Id;
        var actual = participant.Items.Where(i => i.Dish.Id == dishId).Sum(i => i.Quantity);

        await Assert.That(actual).IsEqualTo(portions).Because($"{guest}'s cart: {Describe(participant)}");
    }

    [Then("{string} is listed without dishes")]
    public async Task ThenGuestHasNoDishes(string guest)
    {
        var participant = await ParticipantAsync(guest);
        await Assert.That(participant.Items.Count).IsEqualTo(0).Because($"{guest}'s cart: {Describe(participant)}");
        await Assert.That(participant.Total).IsEqualTo(0m);
    }

    [Then("{string} is marked as confirmed")]
    public async Task ThenGuestIsConfirmed(string guest) =>
        await Assert.That((await ParticipantAsync(guest)).IsSubmitted).IsTrue();

    [Then("{string} is marked as not confirmed yet")]
    public async Task ThenGuestIsNotConfirmed(string guest) =>
        await Assert.That((await ParticipantAsync(guest)).IsSubmitted).IsFalse();

    [Then("every participant's total matches their dishes")]
    public async Task ThenEveryTotalMatches()
    {
        foreach (var participant in (await LatestSnapshotAsync()).Participants)
        {
            var expected = participant.Items.Sum(i => i.Quantity * i.Dish.Price);
            await Assert.That(Math.Round(participant.Total, 2)).IsEqualTo(Math.Round(expected, 2))
                .Because($"{participant.Name}'s cart: {Describe(participant)}");
        }
    }

    [Then("the group total is the sum of the participants' totals")]
    public async Task ThenGroupTotalIsTheSum()
    {
        var snapshot = await LatestSnapshotAsync();
        await Assert.That(Math.Round(snapshot.Total, 2)).IsEqualTo(Math.Round(snapshot.Participants.Sum(p => p.Total), 2));
    }

    [Then("the group total is {int}")]
    public async Task ThenGroupTotalIs(int total) =>
        await Assert.That((await LatestSnapshotAsync()).Total).IsEqualTo((decimal)total);

    [Then("the group total has grown by the price of {int} portion(s) of dish {int}")]
    public async Task ThenGroupTotalHasGrown(int portions, int dish)
    {
        var snapshots = Actor.RecallAll<GroupSnapshot>();
        await Assert.That(snapshots.Count).IsGreaterThanOrEqualTo(2).Because("the owner has to look at the list twice");

        var growth = snapshots[^1].Total - snapshots[^2].Total;
        var expected = portions * DishNumber.Resolve(Actor, dish).Price;
        await Assert.That(Math.Round(growth, 2)).IsEqualTo(Math.Round(expected, 2));
    }

    [Then("the total of the remaining participants is the group total")]
    public async Task ThenRemainingTotalIsGroupTotal() => await ThenGroupTotalIsTheSum();

    // ---- confirmation and what the restaurant receives ----

    [Then("the pre-order is passed to the restaurant with:")]
    public async Task ThenPreOrderContains(DataTable expected)
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Succeeded).IsTrue().Because($"response status {outcome.Status}, body: {outcome.Body}");

        var confirmed = Recall<ConfirmedPreOrder>().Dishes.ToDictionary(d => d.DishId, d => d.Quantity, StringComparer.Ordinal);
        await AssertPortionsAsync(expected, confirmed, outcome.Body);
    }

    [Then("dish {int} is not in the pre-order")]
    public async Task ThenDishIsNotInPreOrder(int dish)
    {
        var dishId = DishNumber.Resolve(Actor, dish).Id;
        await Assert.That(Recall<ConfirmedPreOrder>().Dishes.Any(d => d.DishId == dishId)).IsFalse();
    }

    [Then("the waiter sees the confirmed pre-order with:")]
    public async Task ThenWaiterSeesOrder(DataTable expected)
    {
        var order = await Actor.AsksForAsync(new TheOrderTheWaiterSees());
        await Assert.That(order.Found).IsTrue().Because($"the waiter could not open the order: {order.Body}");
        await Assert.That(order.Status).IsEqualTo("SubmittedByCustomer").Because(order.Body);
        await AssertPortionsAsync(expected, order.PortionsByDishId, order.Body);
    }

    [Then("new guests can no longer join the group")]
    public async Task ThenNewGuestsCannotJoin()
    {
        await Actor.AttemptsToAsync(new JoinTheGroup("Latecomer", mustSucceed: false));
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Succeeded).IsFalse().Because($"a new guest could still join: {outcome.Body}");
    }

    // ---- the owner's own place ----

    [Then("the owner gets the same place both times")]
    public async Task ThenOwnerGetsTheSamePlace()
    {
        var places = Actor.RecallAll<OwnerPlaceResponse>();
        await Assert.That(places.Count).IsGreaterThanOrEqualTo(2);
        await Assert.That(places[^1].GuestSessionId).IsEqualTo(places[^2].GuestSessionId);
    }

    [Then("the owner is listed as the owner of the group")]
    public async Task ThenOwnerIsListed()
    {
        var place = Recall<OwnerPlaceResponse>().GuestSessionId;
        var owner = (await LatestSnapshotAsync()).Participants.SingleOrDefault(p => p.Id == place);

        await Assert.That(owner is not null && owner.Type == "Owner").IsTrue()
            .Because($"participants: [{string.Join(", ", (await LatestSnapshotAsync()).Participants.Select(p => $"{p.Name} ({p.Type})"))}]");
    }

    // ---- refusals ----

    [Then("access to the group is refused")]
    public async Task ThenAccessIsRefused()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status is 401 or 403).IsTrue().Because($"response status {outcome.Status}, body: {outcome.Body}");
    }

    [Then("the owner is told the group could not be found")]
    [Then("the owner is told the participant could not be found")]
    public async Task ThenOwnerIsToldNotFound()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(404).Because($"response body was: {outcome.Body}");
    }

    [Then("the owner is told {string}")]
    public async Task ThenOwnerIsTold(string message)
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Status).IsEqualTo(400).Because($"response body was: {outcome.Body}");
        await Assert.That(outcome.Body.Contains(message, StringComparison.Ordinal)).IsTrue()
            .Because($"response body was: {outcome.Body}");
    }

    // ---- helpers ----

    private async Task<GroupSnapshot> LatestSnapshotAsync()
    {
        var outcome = await Actor.AsksForAsync(new TheOutcome());
        await Assert.That(outcome.Succeeded).IsTrue().Because($"response status {outcome.Status}, body: {outcome.Body}");
        return Recall<GroupSnapshot>();
    }

    private async Task<GroupParticipant> ParticipantAsync(string guest)
    {
        var snapshot = await LatestSnapshotAsync();
        var participant = snapshot.Participants.SingleOrDefault(p => p.Name == guest);
        await Assert.That(participant is not null).IsTrue()
            .Because($"{guest} is not listed; listed: [{string.Join(", ", snapshot.Participants.Select(p => p.Name))}]");
        return participant!;
    }

    private async Task AssertPortionsAsync(DataTable expected, IReadOnlyDictionary<string, int> actual, string body)
    {
        var wanted = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var row in expected.Rows)
        {
            var dish = DishNumber.Resolve(Actor, int.Parse(row["dish"], System.Globalization.CultureInfo.InvariantCulture));
            wanted[dish.Id] = int.Parse(row["portions"], System.Globalization.CultureInfo.InvariantCulture);
        }

        var same = wanted.Count == actual.Count && wanted.All(w => actual.TryGetValue(w.Key, out var portions) && portions == w.Value);
        await Assert.That(same).IsTrue().Because($"expected {Describe(wanted)}, got {Describe(actual)}; body: {body}");
    }

    private static string Describe(GroupParticipant participant) =>
        participant.Items.Count == 0
            ? "empty"
            : string.Join(", ", participant.Items.Select(i => $"{i.Quantity} x {i.Dish.Name}"));

    private static string Describe(IReadOnlyDictionary<string, int> portions) =>
        "{" + string.Join(", ", portions.Select(p => $"{p.Key}: {p.Value}")) + "}";
}
