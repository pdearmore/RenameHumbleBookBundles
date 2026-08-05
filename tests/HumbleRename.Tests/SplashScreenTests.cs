using HumbleRename.Cli;

namespace HumbleRename.Tests;

public class SplashScreenTests
{
    /// <summary>
    /// The Ctrl-C goodbye runs from the signal handler, where a throw would be ugly.
    /// This just proves it renders its sign-off without blowing up.
    /// </summary>
    [Fact]
    public void GoodbyeScreenRendersTheBlockSignOff()
    {
        var original = Console.Out;
        try
        {
            var buffer = new StringWriter();
            Console.SetOut(buffer);

            SplashScreen.ShowGoodbye();

            var text = buffer.ToString();
            Assert.Contains("catch you on the flip side", text);
            Assert.Contains("█", text); // the block-letter art was drawn
        }
        finally
        {
            Console.SetOut(original);
        }
    }
}
