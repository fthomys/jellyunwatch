using Jellyfin.Data.Enums;
using Jellyfin.Plugin.JellyUnwatch.Services;
using MediaBrowser.Model.Dto;
using MediaBrowser.Model.Querying;
using Microsoft.AspNetCore.Mvc;
using Microsoft.AspNetCore.Mvc.Controllers;
using Microsoft.AspNetCore.Mvc.Filters;
using Microsoft.Extensions.Logging;

namespace Jellyfin.Plugin.JellyUnwatch.Filters;

public class ResumeVisibilityFilter : IAsyncResultFilter
{
    private const string UserIdClaim = "Jellyfin-UserId";

    private readonly HiddenItemStore _store;
    private readonly ILogger<ResumeVisibilityFilter> _logger;

    public ResumeVisibilityFilter(HiddenItemStore store, ILogger<ResumeVisibilityFilter> logger)
    {
        _store = store;
        _logger = logger;
    }

    public Task OnResultExecutionAsync(ResultExecutingContext context, ResultExecutionDelegate next)
    {
        try
        {
            Apply(context);
        }
        catch (Exception ex)
        {
            _logger.LogError(ex, "Failed to filter hidden items");
        }

        return next();
    }

    private void Apply(ResultExecutingContext context)
    {
        var kind = GetEndpointKind(context);
        if (kind == EndpointKind.None)
        {
            return;
        }

        if (context.Result is not ObjectResult objectResult
            || objectResult.Value is not QueryResult<BaseItemDto> queryResult)
        {
            _logger.LogInformation(
                "Filter reached {Endpoint} but the result was {ResultType}",
                kind,
                context.Result?.GetType().Name ?? "null");
            return;
        }

        var userId = GetUserId(context);
        if (queryResult.Items.Count == 0 || userId.Equals(Guid.Empty))
        {
            _logger.LogInformation(
                "Filter reached {Endpoint} with {Count} items for user {UserId}, nothing to do",
                kind,
                queryResult.Items.Count,
                userId);
            return;
        }

        var hideSeriesFromNextUp = Plugin.Instance?.Configuration.HideSeriesFromNextUp ?? true;

        var visible = queryResult.Items
            .Where(item => !IsHidden(kind, userId, item, hideSeriesFromNextUp))
            .ToArray();

        var removed = queryResult.Items.Count - visible.Length;

        _logger.LogInformation(
            "Filter reached {Endpoint} for user {UserId}, items {Count}, hidden entries {Hidden}, removed {Removed}, ids {Ids}",
            kind,
            userId,
            queryResult.Items.Count,
            _store.GetHidden(userId).Count,
            removed,
            string.Join(", ", queryResult.Items.Take(5).Select(item => $"{item.Id}/{item.Type}/{item.SeriesId}")));

        if (removed == 0)
        {
            return;
        }

        queryResult.Items = visible;
        queryResult.TotalRecordCount = Math.Max(0, queryResult.TotalRecordCount - removed);
    }

    private bool IsHidden(EndpointKind kind, Guid userId, BaseItemDto item, bool hideSeriesFromNextUp)
    {
        var seriesId = item.SeriesId;

        if (kind == EndpointKind.NextUp)
        {
            return _store.IsHiddenFromNextUp(userId, item.Id, seriesId, hideSeriesFromNextUp);
        }

        if (_store.IsHiddenFromResume(userId, item.Id, seriesId))
        {
            return true;
        }

        return item.Type switch
        {
            BaseItemKind.Series => _store.HasSeries(userId, item.Id),
            BaseItemKind.Season => seriesId.HasValue && _store.HasSeries(userId, seriesId.Value),
            _ => false
        };
    }

    private static EndpointKind GetEndpointKind(ResultExecutingContext context)
    {
        if (context.ActionDescriptor is not ControllerActionDescriptor descriptor)
        {
            return EndpointKind.None;
        }

        if (descriptor.ControllerName.Equals("Items", StringComparison.Ordinal)
            && (descriptor.ActionName.Equals("GetResumeItems", StringComparison.Ordinal)
                || descriptor.ActionName.Equals("GetResumeItemsLegacy", StringComparison.Ordinal)))
        {
            return EndpointKind.Resume;
        }

        if (descriptor.ControllerName.Equals("TvShows", StringComparison.Ordinal)
            && descriptor.ActionName.Equals("GetNextUp", StringComparison.Ordinal))
        {
            return EndpointKind.NextUp;
        }

        var template = descriptor.AttributeRouteInfo?.Template;
        if (template is null)
        {
            return EndpointKind.None;
        }

        if (template.EndsWith("Items/Resume", StringComparison.OrdinalIgnoreCase)
            || template.EndsWith("UserItems/Resume", StringComparison.OrdinalIgnoreCase))
        {
            return EndpointKind.Resume;
        }

        if (template.EndsWith("Shows/NextUp", StringComparison.OrdinalIgnoreCase))
        {
            return EndpointKind.NextUp;
        }

        return EndpointKind.None;
    }

    private static Guid GetUserId(ResultExecutingContext context)
    {
        var claim = context.HttpContext.User.FindFirst(UserIdClaim)?.Value;
        if (!string.IsNullOrEmpty(claim) && Guid.TryParse(claim, out var claimUserId))
        {
            return claimUserId;
        }

        if (context.RouteData.Values.TryGetValue("userId", out var routeValue)
            && Guid.TryParse(routeValue?.ToString(), out var routeUserId))
        {
            return routeUserId;
        }

        var queryValue = context.HttpContext.Request.Query["userId"].ToString();
        return Guid.TryParse(queryValue, out var queryUserId) ? queryUserId : Guid.Empty;
    }

    private enum EndpointKind
    {
        None = 0,
        Resume = 1,
        NextUp = 2
    }
}
