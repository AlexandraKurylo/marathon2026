namespace Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

public sealed record GroupParticipant(
    string? Id,
    string Type,
    string Name,
    IReadOnlyList<ParticipantItem> Items,
    decimal Total,
    bool IsSubmitted);
