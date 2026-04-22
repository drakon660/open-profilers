using Mongo.Profiler.SampleConsoleApp.Models;
using Spectre.Console;

namespace Mongo.Profiler.SampleConsoleApp.ConsoleUi;

internal static class SampleConsoleRunner
{
    public static async Task RunAsync(SampleContext context, IReadOnlyList<SampleCommand> commands)
    {
        while (true)
        {
            var selected = AnsiConsole.Prompt(
                new SelectionPrompt<SampleCommand>()
                    .Title("[green]Choose a Mongo operation to profile[/]")
                    .PageSize(18)
                    .MoreChoicesText("[grey](move up and down for more commands)[/]")
                    .UseConverter(command => $"{command.Category} | {command.Name}")
                    .AddChoices(commands));

            if (selected.IsExit)
                return;

            await RunSelectedCommandAsync(context, selected);
        }
    }

    private static async Task RunSelectedCommandAsync(SampleContext context, SampleCommand selected)
    {
        AnsiConsole.Write(new Rule($"[yellow]{selected.Name}[/]").RuleStyle("grey"));
        AnsiConsole.MarkupLineInterpolated($"[grey]{selected.Description}[/]");

        try
        {
            var result = await AnsiConsole.Status()
                .Spinner(Spinner.Known.Dots)
                .StartAsync("Running command...", _ => selected.RunAsync(context));

            CommandResultRenderer.Write(result);
        }
        catch (Exception exception)
        {
            AnsiConsole.MarkupLine("[red]Command failed.[/]");
            AnsiConsole.WriteException(exception, ExceptionFormats.ShortenEverything);
            AnsiConsole.MarkupLine("[grey]The profiler relay is still running; check the viewer for failure/connectivity events.[/]");
        }

        AnsiConsole.WriteLine();
        AnsiConsole.Prompt(new TextPrompt<string>("[grey]Press Enter to continue[/]").AllowEmpty());
        AnsiConsole.Clear();
    }
}
