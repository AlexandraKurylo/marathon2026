namespace Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

/// <summary>What the owner sees for a group: every participant with their cart, and the sum over all of them.</summary>
public sealed record GroupSnapshot(IReadOnlyList<GroupParticipant> Participants, decimal Total);
