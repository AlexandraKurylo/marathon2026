using Amazon.Lambda.APIGatewayEvents;
using Amazon.Lambda.Core;
using APIHandler.Exceptions;
using APIHandler.Helpers;
using APIHandler.Models.GroupReservation;
using APIHandler.Services.GuestSessions;
using System.Text.Json;
using System.Threading.Tasks;

namespace APIHandler.Handlers.GroupReservations;

public class SubmitGroupPreOrderHandler(IGuestSessionService guestSessionService) : IApiRequestHandler
{
    public async Task<APIGatewayProxyResponse> HandleRequest(APIGatewayProxyRequest request, ILambdaContext context)
    {
        if (!request.TryGetPathValue("groupReservationId", out var groupReservationId))
            return ResponseBuilder.CreateErrorResponse(400, "Missing 'groupReservationId' in path parameters.", context.Logger);

        if (request.RequestContext?.Authorizer?.Claims == null ||
            !request.RequestContext.Authorizer.Claims.TryGetValue("sub", out var userId))
            return ResponseBuilder.CreateErrorResponse(401, "Unauthorized.", context.Logger);

        SubmitGroupPreOrderRequest? body;
        try
        {
            body = string.IsNullOrWhiteSpace(request.Body)
                ? null
                : JsonSerializer.Deserialize<SubmitGroupPreOrderRequest>(request.Body);
        }
        catch (JsonException)
        {
            return ResponseBuilder.CreateErrorResponse(400, "Invalid request body format.", context.Logger);
        }

        if (string.IsNullOrWhiteSpace(body?.ReservationId))
            return ResponseBuilder.CreateErrorResponse(400, "'reservationId' is required.", context.Logger);

        try
        {
            var dishes = await guestSessionService.SubmitGroupPreOrderAsync(userId, groupReservationId, body.ReservationId);
            return ResponseBuilder.CreateSuccessResponse(dishes);
        }
        catch (GroupReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ReservationNotFoundException ex)
        {
            return ResponseBuilder.CreateErrorResponse(404, ex.Message, context.Logger);
        }
        catch (ActionForbiddenException ex)
        {
            return ResponseBuilder.CreateErrorResponse(403, ex.Message, context.Logger);
        }
        catch (ValidationException ex)
        {
            return ResponseBuilder.CreateErrorCodeResponse(ex);
        }
    }
}
