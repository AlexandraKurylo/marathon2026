namespace Restaurant_Aqa_Framework.Screenplay;

/// <summary>A signed-in account taking part in a group pre-order scenario, remembered under its role.</summary>
public sealed record PartyAccount(string Email, string Password, string IdToken);

/// <summary>A booking that has a group pre-order: the reservation, its group and the invitation guests follow.</summary>
public sealed record GroupBooking(string ReservationId, string GroupReservationId, string InviteToken);

/// <summary>A guest who followed the invitation; the session token is what authorises their cart.</summary>
public sealed record JoinedGuest(string Name, string SessionId, string SessionToken);

/// <summary>The roles of a group pre-order scenario and the credentials each of them calls the service with.</summary>
public static class Party
{
    public const string Owner = "owner";
    public const string AnotherCustomer = "another customer";
    public const string Waiter = "waiter";
    public const string Visitor = "visitor";

    /// <summary>The bearer token <paramref name="who"/> calls with: none for a visitor, the id token of a signed-in
    /// account, or the session token of a guest who joined under that name.</summary>
    public static string? TokenOf(Actor actor, string who) => who switch
    {
        Visitor => null,
        Owner or AnotherCustomer or Waiter => actor.Recall<PartyAccount>(who).IdToken,
        _ => actor.Recall<JoinedGuest>(who).SessionToken,
    };
}
