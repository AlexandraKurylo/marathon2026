using System.Text.Json.Serialization;

namespace APIHandler.Models.GroupReservation;

public record OwnerGuestSessionIdResponse(
    [property: JsonPropertyName("guestSessionId")] string GuestSessionId);

public class SubmitGroupPreOrderRequest
{
    [JsonPropertyName("reservationId")]
    public string? ReservationId { get; set; }
}

public record SubmittedDishResponse(
    [property: JsonPropertyName("dishId")] string DishId,
    [property: JsonPropertyName("quantity")] int Quantity);
