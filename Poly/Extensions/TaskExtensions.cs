namespace Poly;

public static class TaskExtensions
{
    public static void Wait(this Task task) => task
        .GetAwaiter()
        .GetResult();

    public static async Task IgnoreErrors(
        this Task task,
        Action<Exception>? errorHandler = default)
    {
        try {
            await task;
        }
        catch (Exception error) {
            errorHandler?.Invoke(error);
        }
    }
}