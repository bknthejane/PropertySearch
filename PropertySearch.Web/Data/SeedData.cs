using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
using Microsoft.EntityFrameworkCore.Storage;
using PropertySearch.Web.Models;
using System.Data;
using System.Diagnostics;

namespace PropertySearch.Web.Data
{
    public static class SeedData
    {
        private const int PropertyCount = 250_000;

        // Fixed seed, so the distribution is identical on any machine and
        // timings are comparable between runs.
        private const int RandomSeed = 20260911;

        private static readonly (string Province, string[] Suburbs)[] Regions =
        [
            ("Gauteng", ["Sandton", "Rosebank", "Midrand", "Fourways", "Randburg",
                         "Soweto", "Centurion", "Pretoria East", "Benoni", "Boksburg"]),
            ("Western Cape", ["Sea Point", "Claremont", "Stellenbosch", "Somerset West",
                              "Durbanville", "Table View", "Constantia", "Paarl"]),
            ("KwaZulu-Natal", ["Umhlanga", "Ballito", "Westville", "Hillcrest",
                               "Pinetown", "Amanzimtoti"]),
            ("Eastern Cape", ["Summerstrand", "Walmer", "Gonubie", "Beacon Bay"]),
            ("Free State", ["Westdene", "Langenhoven Park", "Universitas"]),
            ("Mpumalanga", ["Nelspruit Central", "White River", "Secunda"]),
            ("Limpopo", ["Bendor", "Flora Park"]),
            ("North West", ["Baillie Park", "Mooivallei Park"]),
            ("Northern Cape", ["Royldene", "Hadison Park"])
        ];

        public static async Task SeedAsync(PropertyDbContext context)
        {
            // One transaction for the whole seed: an application lock, the
            // regions, and all 250,000 properties. A run that dies partway
            // rolls back completely, so the next startup sees an empty
            // Properties table and seeds again rather than treating a partial
            // load as done.
            await using var transaction = await context.Database.BeginTransactionAsync();

            // Serialises concurrent startups. Held until the transaction ends,
            // so it is released by the commit or the rollback either way — the
            // second process then sees the committed rows and returns.
            await AcquireSeedLockAsync(context);

            if (await context.Properties.AnyAsync())
                return;

            var suburbIds = await SeedRegionsAsync(context);
            await BulkInsertPropertiesAsync(context, suburbIds);

            await transaction.CommitAsync();
        }

        private static Task AcquireSeedLockAsync(PropertyDbContext context) =>
            context.Database.ExecuteSqlRawAsync(
                """
                DECLARE @result int;
                EXEC @result = sp_getapplock
                    @Resource = 'PropertySearch:Seed',
                    @LockMode = 'Exclusive',
                    @LockOwner = 'Transaction',
                    @LockTimeout = 300000;
                IF @result < 0
                    THROW 50000, 'Could not acquire the PropertySearch seed lock.', 1;
                """);

        private static async Task<int[]> SeedRegionsAsync(PropertyDbContext context)
        {
            // Reconciled by name rather than skipped wholesale on "are there
            // any provinces?" — a half-written region set would otherwise
            // leave us with provinces but no suburbs to hang properties on.
            foreach (var (provinceName, suburbNames) in Regions)
            {
                var province = await context.Provinces
                    .Include(p => p.Suburbs)
                    .FirstOrDefaultAsync(p => p.Name == provinceName);

                if (province is null)
                {
                    province = new Province { Name = provinceName };
                    context.Provinces.Add(province);
                }

                foreach (var suburbName in suburbNames)
                {
                    if (!province.Suburbs.Any(s => s.Name == suburbName))
                        province.Suburbs.Add(new Suburb { Name = suburbName });
                }
            }

            // ~45 rows. EF Core is the right tool at this size — change
            // tracking costs nothing here and it resolves the FK graph for us.
            await context.SaveChangesAsync();

            var suburbIds = await context.Suburbs.Select(s => s.Id).ToArrayAsync();

            if (suburbIds.Length == 0)
                throw new InvalidOperationException(
                    "Region seeding produced no suburbs to assign properties to.");

            return suburbIds;
        }

        private static async Task BulkInsertPropertiesAsync(PropertyDbContext context, int[] suburbIds)
        {
            var stopwatch = Stopwatch.StartNew();

            var table = BuildPropertyTable(suburbIds);

            // The context's own connection and transaction, so the bulk copy
            // commits or rolls back with the regions it depends on.
            var connection = (SqlConnection)context.Database.GetDbConnection();
            var transaction = (SqlTransaction)context.Database.CurrentTransaction!.GetDbTransaction();

            using var bulkCopy = new SqlBulkCopy(connection, SqlBulkCopyOptions.Default, transaction)
            {
                DestinationTableName = "Properties",
                BatchSize = 10_000,
                BulkCopyTimeout = 300
            };

            foreach (DataColumn column in table.Columns)
                bulkCopy.ColumnMappings.Add(column.ColumnName, column.ColumnName);

            await bulkCopy.WriteToServerAsync(table);

            stopwatch.Stop();
            Console.WriteLine(
                $"Seeded {PropertyCount:N0} properties in {stopwatch.ElapsedMilliseconds:N0} ms");
        }

        private static DataTable BuildPropertyTable(int[] suburbIds)
        {
            var random = new Random(RandomSeed);
            var table = new DataTable();

            // Id is omitted — it is an IDENTITY column, and without
            // SqlBulkCopyOptions.KeepIdentity SQL Server generates the values.
            table.Columns.Add("Title", typeof(string));
            table.Columns.Add("Description", typeof(string));
            table.Columns.Add("PropertyType", typeof(int));
            table.Columns.Add("ListingType", typeof(int));
            table.Columns.Add("Price", typeof(decimal));
            table.Columns.Add("Bedrooms", typeof(int));
            table.Columns.Add("Bathrooms", typeof(int));
            table.Columns.Add("Garages", typeof(int));
            table.Columns.Add("FloorAreaSqm", typeof(int));
            table.Columns.Add("ErfSizeSqm", typeof(int));
            table.Columns.Add("ListedOn", typeof(DateTime));
            table.Columns.Add("IsActive", typeof(bool));
            table.Columns.Add("SuburbId", typeof(int));

            var today = DateTime.UtcNow.Date;

            for (var i = 0; i < PropertyCount; i++)
            {
                var propertyType = PickPropertyType(random);
                var bedrooms = PickBedrooms(random, propertyType);
                var price = PickPrice(random, propertyType, bedrooms);
                var listingType = random.Next(100) < 80
                    ? ListingType.ForSale
                    : ListingType.ToRent;

                var row = table.NewRow();

                row["Title"] = $"{bedrooms} bedroom {propertyType} " +
                               (listingType == ListingType.ForSale ? "for sale" : "to rent");
                row["Description"] = DBNull.Value;
                row["PropertyType"] = (int)propertyType;
                row["ListingType"] = (int)listingType;
                row["Price"] = price;
                row["Bedrooms"] = bedrooms;
                row["Bathrooms"] = Math.Max(1, bedrooms - random.Next(0, 2));
                row["Garages"] = random.Next(0, 4);
                row["FloorAreaSqm"] = 45 + (bedrooms * random.Next(25, 60));
                row["ErfSizeSqm"] = propertyType == PropertyType.Apartment
                    ? DBNull.Value
                    : 200 + random.Next(0, 1800);
                row["ListedOn"] = today.AddDays(-random.Next(0, 730));
                row["IsActive"] = random.Next(100) < 85;
                row["SuburbId"] = suburbIds[random.Next(suburbIds.Length)];

                table.Rows.Add(row);
            }

            return table;
        }

        private static PropertyType PickPropertyType(Random random) =>
            random.Next(100) switch
            {
                < 45 => PropertyType.House,
                < 70 => PropertyType.Apartment,
                < 85 => PropertyType.Townhouse,
                < 92 => PropertyType.VacantLand,
                < 97 => PropertyType.Commercial,
                _ => PropertyType.Farm
            };

        private static int PickBedrooms(Random random, PropertyType propertyType)
        {
            if (propertyType is PropertyType.VacantLand or PropertyType.Commercial)
                return 0;

            return random.Next(100) switch
            {
                < 10 => 1,
                < 40 => 2,
                < 75 => 3,
                < 92 => 4,
                _ => 5
            };
        }

        private static decimal PickPrice(Random random, PropertyType propertyType, int bedrooms)
        {
            var baseline = propertyType switch
            {
                PropertyType.Apartment => 750_000m,
                PropertyType.Townhouse => 1_200_000m,
                PropertyType.House => 1_600_000m,
                PropertyType.VacantLand => 600_000m,
                PropertyType.Commercial => 4_500_000m,
                _ => 3_000_000m
            };

            var byBedrooms = baseline + (bedrooms * 450_000m);
            var variance = (decimal)(random.NextDouble() * 1.4 + 0.5);

            return Math.Round(byBedrooms * variance / 1000m) * 1000m;
        }
    }
}