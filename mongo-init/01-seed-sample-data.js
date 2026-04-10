const dbName = "profiler_samples";
const collectionName = "orders";

db = db.getSiblingDB(dbName);

if (db.getCollection(collectionName).countDocuments({}) === 0) {
  db.getCollection(collectionName).insertMany([
    {
      customer: "Alice",
      city: "London",
      status: "paid",
      amount: NumberDecimal("125.50"),
      orderedAt: ISODate("2026-03-20T08:30:00Z")
    },
    {
      customer: "Bob",
      city: "London",
      status: "pending",
      amount: NumberDecimal("80.00"),
      orderedAt: ISODate("2026-03-21T10:15:00Z")
    },
    {
      customer: "Carla",
      city: "Berlin",
      status: "paid",
      amount: NumberDecimal("230.00"),
      orderedAt: ISODate("2026-03-22T09:00:00Z")
    },
    {
      customer: "Dan",
      city: "Prague",
      status: "paid",
      amount: NumberDecimal("42.00"),
      orderedAt: ISODate("2026-03-22T11:45:00Z")
    }
  ]);
}
