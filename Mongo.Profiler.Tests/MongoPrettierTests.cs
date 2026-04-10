using AwesomeAssertions;
using MongoDB.Bson;
using MongoDB.Driver;

namespace Mongo.Profiler.Tests;

public class MongoPrettierTests
{
    [Fact]
    public void TestFind()
    {
        string query = "{ \"find\" : \"orders\", \"filter\" : { }, \"limit\" : 21, \"batchSize\" : 21, \"$db\" : \"profiler_samples\", \"lsid\" : { \"id\" : UUID(\"1a3be927-be74-4588-95ee-1908aab06fc2\") } }";
        var queryFixed  = MongoQueryPrettier.Prettify(query);

        queryFixed.Should().Be("db.orders.find({}).limit(21)");
    }
    
    [Fact]
    public void TestFind2()
    {
        string query = "{ \"find\" : \"orders\", \"filter\" : { \"Amount\" : { \"$gte\" : NumberDecimal(\"90\") }, \"Status\" : \"paid\", \"OrderedAt\" : { \"$gte\" : ISODate(\"2026-03-21T00:00:00Z\") } }, \"sort\" : { \"Amount\" : -1 }, \"projection\" : { \"Customer\" : 1, \"City\" : 1, \"Amount\" : 1, \"OrderedAt\" : 1, \"_id\" : 0 }, \"limit\" : 3, \"batchSize\" : 21, \"$db\" : \"profiler_samples\", \"lsid\" : { \"id\" : UUID(\"9dd0892e-e8d0-44b6-ae6f-9146d10851d0\") } }";
        var queryFixed  = MongoQueryPrettier.Prettify(query);

        queryFixed.Should().Be("db.orders.find({Amount:{$gte:90},  \"Status\" : \"paid\",  \"OrderedAt\" : {\n    \"$gte\" : ISODate(\"2026-03-21T00:00:00Z\")\n  }}, {\n  \"Customer\" : 1,\n  \"City\" : 1,\n  \"Amount\" : 1,\n  \"OrderedAt\" : 1,\n  \"_id\" : 0\n}).sort({\n  \"Amount\" : -1\n}).limit(3)");
    }

    [Fact]
    public void TestProfileReaderCommentConstant()
    {
        MongoSystemProfileReader.ReaderComment.Should().Be("mongo-profiler-direct-profile-reader");
    }

    [Fact]
    public async Task TestProfileReaderBootstrapAsync()
    {
        const string connectionString = "mongodb://localhost:27017";
        const string databaseName = "profiler_samples";

        var client = new MongoClient(connectionString);
        var database = client.GetDatabase(databaseName);

        var checkpoint = await MongoSystemProfileReader.BootstrapAsync(
            database,
            MongoSystemProfileReader.ReaderComment,
            CancellationToken.None);

        
        var page = await MongoSystemProfileReader.ReadNextPageAsync(database,  checkpoint, 100, CancellationToken.None);
        
        checkpoint.Should().NotBeNull();
    }

    [Fact]
    public void TestProfileReaderExcludeSystemProfileFilter()
    {
        var filter = MongoSystemProfileReader.BuildExcludeSystemProfileNamespaceFilter("profiler_samples");
        var expected = new BsonDocument("ns",
            new BsonDocument("$not", new BsonRegularExpression("^profiler_samples\\.system\\.profile$", "i")));

        filter.Should().BeEquivalentTo(expected);
    }
}
