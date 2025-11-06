using System.Diagnostics;

namespace Sage200Microservice.Services.Tracing
{
    /// <summary>
    /// Helper class for distributed tracing in the services layer
    /// </summary>
    public static class TracingHelper
    {
        private static readonly ActivitySource _activitySource =
            new ActivitySource("Sage200Microservice.Services", "1.0.0");

        private static void MarkException(Activity? activity, Exception ex)
        {
            if (activity == null) return;

            activity.SetStatus(ActivityStatusCode.Error, ex.Message);
            activity.SetTag("exception.type", ex.GetType().FullName);
            activity.SetTag("exception.message", ex.Message);
            activity.SetTag("exception.stacktrace", ex.StackTrace);

#if NET6_0_OR_GREATER
            var tags = new ActivityTagsCollection
            {
                { "exception.type", ex.GetType().FullName },
                { "exception.message", ex.Message },
                { "exception.stacktrace", ex.StackTrace }
            };
            activity.AddEvent(new ActivityEvent("exception", tags: tags));
#else
            activity.AddEvent(new ActivityEvent("exception"));
#endif
        }

        public static T TraceOperation<T>(string operationName, Func<T> operation, Dictionary<string, object>? tags = null)
        {
            using var activity = _activitySource.StartActivity(operationName, ActivityKind.Internal);

            if (activity != null && tags != null)
                foreach (var t in tags) activity.SetTag(t.Key, t.Value);

            try
            {
                var result = operation();
                activity?.SetStatus(ActivityStatusCode.Ok);
                return result;
            }
            catch (Exception ex)
            {
                MarkException(activity, ex);
                throw;
            }
        }

        public static async Task<T> TraceOperationAsync<T>(string operationName, Func<Task<T>> operation, Dictionary<string, object>? tags = null)
        {
            using var activity = _activitySource.StartActivity(operationName, ActivityKind.Internal);

            if (activity != null && tags != null)
                foreach (var t in tags) activity.SetTag(t.Key, t.Value);

            try
            {
                var result = await operation();
                activity?.SetStatus(ActivityStatusCode.Ok);
                return result;
            }
            catch (Exception ex)
            {
                MarkException(activity, ex);
                throw;
            }
        }

        public static async Task TraceOperationAsync(string operationName, Func<Task> operation, Dictionary<string, object>? tags = null)
        {
            using var activity = _activitySource.StartActivity(operationName, ActivityKind.Internal);

            if (activity != null && tags != null)
                foreach (var t in tags) activity.SetTag(t.Key, t.Value);

            try
            {
                await operation();
                activity?.SetStatus(ActivityStatusCode.Ok);
            }
            catch (Exception ex)
            {
                MarkException(activity, ex);
                throw;
            }
        }

        public static Activity? CreateChildActivity(string operationName, Dictionary<string, object>? tags = null)
        {
            var activity = _activitySource.StartActivity(operationName, ActivityKind.Internal);

            if (activity != null && tags != null)
                foreach (var t in tags) activity.SetTag(t.Key, t.Value);

            return activity;
        }
    }
}