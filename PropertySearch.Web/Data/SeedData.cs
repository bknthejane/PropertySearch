using Microsoft.Data.SqlClient;
using Microsoft.EntityFrameworkCore;
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

        public static async Task SeedAsync(PropertyDbContext context, string connectionString)
        {
            if (await context.Properties.AnyAsync())
                return;

            var suburbIds = await SeedRegionsAsync(context);
            await BulkInsertPropertiesAsync(connectionString, suburbIds);
        }

        private static async Task<int[]> SeedRegionsAsync(PropertyDbContext context)
        {
            if (await context.Provinces.AnyAsync())
                return await context.Suburbs.Select(s => s.Id).ToArrayAsync();

            foreach (var (provinceName, suburbNames) in Regions)
            {
                var province = new Province { Name = provinceName };
                context.Provinces.Add(province);

                foreach (var suburbName in suburbNames)
                {
                    province.Suburbs.Add(new Suburb { Name = suburbName });
                }
            }

            // ~45 rows. EF Core is the right tool at this size — change
            // tracking costs nothing here and it resolves the FK graph for us.
            await context.SaveChangesAsync();

            return await context.Suburbs.Select(s => s.Id).ToArrayAsync();
        }

        private static async Task BulkInsertPropertiesAsync(string connectionString, int[] suburbIds)
        {
            var stopwatch = Stopwatch.StartNew();

            var table = BuildPropertyTable(suburbIds);

            await using var connection = new SqlConnection(connectionString);
            await connection.OpenAsync();

            using var bulkCopy = new SqlBulkCopy(connection)
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

                var row = table.NewRow();

                row["Title"] = $"{bedrooms} bedroom {propertyType} for sale";
                row["Description"] = DBNull.Value;
                row["PropertyType"] = (int)propertyType;
                row["ListingType"] = random.Next(100) < 80
                    ? (int)ListingType.ForSale
                    : (int)ListingType.ToRent;
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