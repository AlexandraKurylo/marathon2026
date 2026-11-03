using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Dishes;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>A guest puts portions of one of the scenario's dishes (numbered from 1 in the order they were added to
/// the menu) into their own cart.</summary>
public sealed class PutADishInTheCart(string guestName, int dishNumber, int portions) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var guest = actor.Recall<JoinedGuest>(guestName);
        var dish = DishNumber.Resolve(actor, dishNumber);
        var response = actor.TheLastResponse();

        await response.CaptureAsync(await new GroupPreOrderApi(actor.UsingTheApi())
            .AddDishAsync(guest.SessionId, guest.SessionToken, new CartDishRequest(dish.Id, portions)));
        if (!response.Succeeded)
            throw new InvalidOperationException($"{guestName} could not add dish {dishNumber}: {response.Status} {response.Body}");
    }
}

/// <summary>Dishes are referred to by number in the feature files: dish 1 is the first one the scenario added.</summary>
public static class DishNumber
{
    public static DishResponse Resolve(Actor actor, int dishNumber)
    {
        var dishes = actor.RecallAll<DishResponse>();
        if (dishNumber < 1 || dishNumber > dishes.Count)
            throw new InvalidOperationException($"The scenario has {dishes.Count} dish(es); dish {dishNumber} was asked for.");

        return dishes[dishNumber - 1];
    }
}
