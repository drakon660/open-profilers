namespace Mongo.Profiler;

public interface IMongoProfilerEventSink
{
    void Publish(MongoProfilerQueryEvent queryEvent);
}
