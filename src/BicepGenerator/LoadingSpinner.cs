namespace BicepGenerator;

/// <summary>
/// Displays an animated loading spinner in the console
/// </summary>
public class LoadingSpinner : IDisposable
{
    private readonly CancellationTokenSource _cancellationTokenSource = new();
    private readonly Task _spinnerTask;
    private readonly string[] _frames = ["⠋", "⠙", "⠹", "⠸", "⠼", "⠴", "⠦", "⠧", "⠇", "⠏"];
    private readonly string _message;

    public LoadingSpinner(string message = "Processing")
    {
        _message = message;
        Console.CursorVisible = false;
        _spinnerTask = Task.Run(SpinAsync);
    }

    private async Task SpinAsync()
    {
        var frameIndex = 0;
        var startLeft = Console.CursorLeft;
        var startTop = Console.CursorTop;

        try
        {
            while (!_cancellationTokenSource.Token.IsCancellationRequested)
            {
                Console.SetCursorPosition(startLeft, startTop);
                Console.Write($"{_frames[frameIndex]} {_message}...");
                frameIndex = (frameIndex + 1) % _frames.Length;
                await Task.Delay(80, _cancellationTokenSource.Token);
            }
        }
        catch (OperationCanceledException)
        {
            // Expected when stopping the spinner
        }
        finally
        {
            Console.SetCursorPosition(startLeft, startTop);
            Console.Write(new string(' ', _message.Length + 20)); // Clear the line
            Console.SetCursorPosition(startLeft, startTop);
        }
    }

    public void Dispose()
    {
        _cancellationTokenSource.Cancel();
        try
        {
            _spinnerTask.Wait(TimeSpan.FromSeconds(1));
        }
        catch
        {
            // Ignore timeout
        }
        Console.CursorVisible = true;
        _cancellationTokenSource.Dispose();
    }
}
