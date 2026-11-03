using APIHandler.Constants;
using APIHandler.Constants.Orders;
using APIHandler.Entities;
using APIHandler.Exceptions;
using APIHandler.Repositories.Auth;
using APIHandler.Repositories.Dishes;
using APIHandler.Repositories.GroupReservations;
using APIHandler.Repositories.GuestSessions;
using APIHandler.Repositories.Orders;
using APIHandler.Repositories.Reservations;
using APIHandler.Repositories.Tables;
using APIHandler.Services.GuestSessions;
using Moq;

namespace APIHandler.Tests.Services;

public class GroupPreOrderServiceTests
{
    private const string OwnerId = "owner-1";
    private const string GroupId = "group-1";
    private const string ReservationId = "reservation-1";

    private readonly Mock<IGuestSessionRepository> _sessions = new();
    private readonly Mock<IGroupReservationRepository> _groups = new();
    private readonly Mock<IDishRepository> _dishes = new();
    private readonly Mock<IReservationRepository> _reservations = new();
    private readonly Mock<IUserRepository> _users = new();
    private readonly Mock<ITableRepository> _tables = new();
    private readonly Mock<IOrderRepository> _orders = new();
    private readonly GuestSessionService _service;

    private readonly GroupReservation _group = new()
    {
        GroupReservationId = GroupId,
        ReservationId = ReservationId,
        ReservationToken = "token",
        Status = GroupReservationStatus.Open,
    };

    private readonly Reservation _reservation = new()
    {
        Id = ReservationId,
        TableId = "table-1",
        WaiterId = "waiter-1",
        CustomerId = OwnerId,
        GuestCount = 6,
        Status = ReservationStatus.Reserved,
    };

    public GroupPreOrderServiceTests()
    {
        _service = new GuestSessionService(
            _groups.Object, _sessions.Object, _dishes.Object, _reservations.Object, _users.Object, _tables.Object, _orders.Object);

        _groups.Setup(r => r.GetGroupReservationByIdAsync(GroupId)).ReturnsAsync(_group);
        _groups.Setup(r => r.GetByReservationIdAsync(ReservationId)).ReturnsAsync(_group);
        _reservations.Setup(r => r.GetReservationByIdAsync(ReservationId)).ReturnsAsync(_reservation);
        _dishes.Setup(r => r.GetDishesByIdsAsync(It.IsAny<IEnumerable<string>>())).ReturnsAsync(new List<Dish>());
    }

    [Fact]
    public async Task SubmitGroupPreOrderAsync_MergesSubmittedCartsOnly()
    {
        var order = NewOrder();
        _orders.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(order);
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(new List<GuestSession>
        {
            Session("a", true, ("dish-1", 2)),
            Session("b", true, ("dish-1", 1), ("dish-2", 1)),
            Session("c", false, ("dish-3", 5)),
        });

        var result = await _service.SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId);

        Assert.Equal(2, result.Count);
        Assert.Equal(3, result.Single(d => d.DishId == "dish-1").Quantity);
        Assert.Equal(1, result.Single(d => d.DishId == "dish-2").Quantity);
        Assert.DoesNotContain(result, d => d.DishId == "dish-3");
        Assert.Equal(OrderStatus.SubmittedByCustomer, order.Status);
        Assert.Equal(GroupReservationStatus.Submitted, _group.Status);
        _orders.Verify(r => r.SaveOrderAsync(order), Times.Once);
        _groups.Verify(r => r.SaveGroupReservationAsync(_group), Times.Once);
    }

    [Fact]
    public async Task SubmitGroupPreOrderAsync_NoSubmittedCarts_ThrowsValidationException()
    {
        _orders.Setup(r => r.GetOrderByReservationIdAsync(ReservationId)).ReturnsAsync(NewOrder());
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(new List<GuestSession> { Session("a", false, ("dish-1", 1)) });

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId));
    }

    [Fact]
    public async Task SubmitGroupPreOrderAsync_OrderMissing_ThrowsValidationException()
    {
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(new List<GuestSession> { Session("a", true, ("dish-1", 1)) });

        await Assert.ThrowsAsync<ValidationException>(() => _service.SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId));
    }

    [Fact]
    public async Task SubmitGroupPreOrderAsync_NotOwner_ThrowsActionForbidden()
    {
        await Assert.ThrowsAsync<ActionForbiddenException>(() => _service.SubmitGroupPreOrderAsync("someone-else", GroupId, ReservationId));
    }

    [Fact]
    public async Task SubmitGroupPreOrderAsync_AlreadySubmitted_ThrowsGroupReservationSubmitted()
    {
        _group.Status = GroupReservationStatus.Submitted;

        await Assert.ThrowsAsync<GroupReservationSubmittedException>(() => _service.SubmitGroupPreOrderAsync(OwnerId, GroupId, ReservationId));
    }

    [Fact]
    public async Task GetGroupSessionsAsync_EmptyGroup_ReturnsNoParticipants()
    {
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(new List<GuestSession>());

        var snapshot = await _service.GetGroupSessionsAsync(OwnerId, GroupId);

        Assert.Empty(snapshot.Participants);
        Assert.Equal(0m, snapshot.Total);
    }

    [Fact]
    public async Task GetOwnerGuestSessionIdAsync_NoOwnerSession_CreatesOne()
    {
        _sessions.Setup(r => r.GetAllByGroupReservationIdAsync(GroupId)).ReturnsAsync(new List<GuestSession>());

        var response = await _service.GetOwnerGuestSessionIdAsync(OwnerId, ReservationId);

        Assert.False(string.IsNullOrEmpty(response.GuestSessionId));
        _sessions.Verify(r => r.SaveAsync(It.Is<GuestSession>(s => s.IsOwnerSession && s.OwnerUserId == OwnerId)), Times.Once);
    }

    private static Order NewOrder() => new()
    {
        OrderId = "order-1",
        CustomerId = OwnerId,
        ReservationId = ReservationId,
        ReservationAddress = "address",
        ReservationTime = "time",
        Dishes = [],
        Status = OrderStatus.Created,
    };

    private static GuestSession Session(string id, bool submitted, params (string DishId, int Quantity)[] dishes) => new()
    {
        Id = id,
        GroupReservationId = GroupId,
        GuestName = id,
        IsSubmitted = submitted,
        Dishes = dishes.Select(d => new OrderItem { DishId = d.DishId, Quantity = d.Quantity, State = OrderItemState.Pending }).ToList(),
    };
}
