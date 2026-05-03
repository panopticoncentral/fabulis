using Fabulis.Server.Auth;
using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

public static class ModelEndpoints
{
    public static IEndpointRouteBuilder MapModelEndpoints(this IEndpointRouteBuilder routes)
    {
        var group = routes.MapGroup("/models").RequireSession();

        group.MapGet("", async (OpenRouterService openRouter) =>
        {
            try
            {
                var models = await openRouter.GetModelsAsync();
                return Results.Ok(models.Select(m => new ModelInfoDto(m.Id, m.Name)).ToList());
            }
            catch (Exception ex)
            {
                return Results.Problem(detail: ex.Message, statusCode: 502);
            }
        });

        return routes;
    }
}
