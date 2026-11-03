using Microsoft.Playwright;
using Restaurant_Aqa_Framework.Api.Dtos.GroupPreOrder;

namespace Restaurant_Aqa_Framework.Api;

/// <summary>The waiters list: adding a registered user to it makes that user a waiter of the location.</summary>
public sealed class WaitersApi(IAPIRequestContext api) : ApiClientBase(api)
{
    public Task<IAPIResponse> AddWaiterAsync(WaiterAssignmentRequest request) =>
        Api.PostAsync(Url("/waiters"), new APIRequestContextOptions
        {
            DataString = Serialize(request),
            Headers = Headers(),
        });

    public Task<IAPIResponse> RemoveWaiterAsync(WaiterRemovalRequest request) =>
        DeleteAsync(Url("/waiters"), new APIRequestContextOptions
        {
            DataString = Serialize(request),
            Headers = Headers(),
        });
}
