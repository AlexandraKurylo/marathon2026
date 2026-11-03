using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;
using Restaurant_Aqa_Framework.Api.Dtos.Orders;
using Restaurant_Aqa_Framework.Api.Dtos.Reservations;
using Restaurant_Aqa_Framework.Api.Dtos.Tables;
using Restaurant_Aqa_Framework.Api.Seeding;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>The owner books one of the scenario's tables (the first one unless told otherwise) for a group, starts
/// an empty pre-order for it unless asked not to, and finds the group behind the invitation. The booking is
/// remembered as the current one; a named second booking is remembered under its name only, so "the group" keeps
/// meaning the first one. The reservation is cancelled again afterwards.</summary>
public sealed class BookATableForAGroup(int guests, int tableNumber = 1, bool withPreOrder = true, string? name = null) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var owner = actor.Recall<PartyAccount>(Party.Owner);
        var tables = actor.RecallAll<TableResponse>();
        if (tables.Count < tableNumber)
            throw new InvalidOperationException($"The scenario has {tables.Count} table(s); table {tableNumber} was asked for.");

        var table = tables[tableNumber - 1];
        var reservations = new ReservationsApi(actor.UsingTheApi());
        var response = actor.TheLastResponse();

        var start = DateTime.UtcNow.Date.AddDays(3).AddHours(10 + (2 * tableNumber));
        await response.CaptureAsync(await reservations.CreateReservationAsync(
            SampleDataFactory.CreateReservationRequest(table.Id, start, guests), owner.IdToken));
        if (!response.Succeeded)
            throw new InvalidOperationException($"Booking the table failed with {response.Status}: {response.Body}");

        var reservation = response.Json<ReservationResponse>();
        actor.Recall<CleanupScope>().TrackDelete(
            $"reservation:{reservation.Id}",
            () => reservations.CancelReservationAsync(reservation.Id, owner.IdToken));

        if (withPreOrder)
        {
            var orders = new OrdersApi(actor.UsingTheApi());
            var order = await orders.CreateOrderAsync(new CreateOrderRequest(reservation.Id, []), owner.IdToken);
            if (!order.Ok)
                throw new InvalidOperationException(
                    $"Starting the pre-order failed with {order.Status}: {await order.ReadErrorMessageAsync()}");
        }

        var groups = new GroupPreOrderApi(actor.UsingTheApi());
        var invite = await groups.GetInviteTokenAsync(reservation.Id, owner.IdToken);
        if (!invite.Ok)
            throw new InvalidOperationException(
                $"Getting the invitation failed with {invite.Status}: {await invite.ReadErrorMessageAsync()}");
        var inviteToken = (await invite.ReadAsAsync<InviteTokenResponse>()).Token;

        var invitation = await groups.GetInvitationAsync(inviteToken);
        if (!invitation.Ok)
            throw new InvalidOperationException(
                $"Opening the invitation failed with {invitation.Status}: {await invitation.ReadErrorMessageAsync()}");
        var groupReservationId = (await invitation.ReadAsAsync<InvitationResponse>()).GroupReservationId;

        var booking = new GroupBooking(reservation.Id, groupReservationId, inviteToken);
        if (name is null)
        {
            actor.Remember(booking);
            return;
        }

        var current = actor.TryRecall<GroupBooking>(out var first) ? first : null;
        actor.Remember(name, booking);
        if (current is not null)
            actor.Remember(current);
    }
}
