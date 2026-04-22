using Mongo.Profiler.SampleConsoleApp.Models;
using MongoDB.Bson;
using MongoDB.Bson.IO;
using MongoDB.Driver;
using Spectre.Console;

namespace Mongo.Profiler.SampleConsoleApp.ConsoleUi;

internal static class WriteResultFormatter
{
    public static CommandResult ToDocument(UpdateResult result)
    {
        return new DocumentResult(new BsonDocument
        {
            ["acknowledged"] = result.IsAcknowledged,
            ["matched"] = result.MatchedCount,
            ["modified"] = result.ModifiedCount,
            ["upsertedId"] = result.UpsertedId ?? BsonNull.Value
        });
    }

    public static CommandResult ToDocument(ReplaceOneResult result)
    {
        return new DocumentResult(new BsonDocument
        {
            ["acknowledged"] = result.IsAcknowledged,
            ["matched"] = result.MatchedCount,
            ["modified"] = result.ModifiedCount,
            ["upsertedId"] = result.UpsertedId ?? BsonNull.Value
        });
    }

    public static CommandResult ToDocument(DeleteResult result)
    {
        return new DocumentResult(new BsonDocument
        {
            ["acknowledged"] = result.IsAcknowledged,
            ["deleted"] = result.DeletedCount
        });
    }
}

internal static class CommandResultRenderer
{
    public static void Write(CommandResult result)
    {
        switch (result)
        {
            case TextResult text:
                AnsiConsole.MarkupLineInterpolated($"[green]{text.Value}[/]");
                break;
            case DocumentResult document:
                WriteJson(document.Value);
                break;
            case DocumentsResult documents:
                WriteDocumentsTable(documents.Values);
                break;
            case JsonResult json:
                WriteJson(json.Value);
                break;
        }
    }

    private static void WriteDocumentsTable(IReadOnlyList<BsonDocument> documents)
    {
        if (documents.Count == 0)
        {
            AnsiConsole.MarkupLine("[yellow]No documents returned.[/]");
            return;
        }

        var table = new Table().RoundedBorder().BorderColor(Color.Grey);
        table.AddColumn("#");
        table.AddColumn("Document");

        for (var index = 0; index < documents.Count; index++)
            table.AddRow((index + 1).ToString(), Markup.Escape(ToJson(documents[index])));

        AnsiConsole.Write(table);
    }

    private static void WriteJson(BsonValue value)
    {
        AnsiConsole.Write(
            new Panel(Markup.Escape(ToJson(value)))
                .Header("Result")
                .BorderColor(Color.Green));
    }

    private static string ToJson(BsonValue value)
    {
        return value.ToJson(new JsonWriterSettings
        {
            Indent = true,
            OutputMode = JsonOutputMode.RelaxedExtendedJson
        });
    }
}
