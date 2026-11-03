using Amazon.DynamoDBv2.DataModel;
using Amazon.Lambda.APIGatewayEvents;
using APIHandler.Constants;
using APIHandler.Constants.Dishes;
using APIHandler.Entities;
using APIHandler.Helpers;
using System.Text.Json;

namespace RestaurantServer;

public sealed class DataSeeder(LambdaBridge bridge, FakeCognito cognito, ILogger<DataSeeder> logger)
{
    public const string DefaultPassword = "Password1!";

    public static readonly (string Email, string FirstName, string LastName, string? Group)[] Users =
    [
        ("admin@example.com", "Anna", "Admin", "Admin"),
        ("waiter1@example.com", "Oleh", "Waiter", null),
        ("waiter2@example.com", "Iryna", "Waiter", null),
        ("waiter3@example.com", "Taras", "Waiter", null),
        ("customer@example.com", "Olena", "Customer", null),
        ("guest@example.com", "Andrii", "Customer", null),
    ];

    private static readonly (string Id, string Address, string Description, double Rating, string Occupancy)[] Locations =
    [
        ("loc-1", "48 Rustaveli Avenue", "Our flagship restaurant in the city centre with a cosy terrace and seasonal menu.", 4.8, "78%"),
        ("loc-2", "14 Deribasivska Street", "A bright family place near the park, famous for fresh salads and desserts.", 4.6, "64%"),
        ("loc-3", "3 Frantsuzkyi Boulevard", "A quiet place by the sea with a large hall for group celebrations.", 4.5, "52%"),
    ];

    private static readonly (string Name, DishType Type, decimal Price, string Weight, string Calories, string Description)[] Menu =
    [
        ("Fresh Greek Salad", DishType.Appetizer, 8.50m, "250 g", "320 kcal", "Tomatoes, cucumbers, feta, olives and red onion with olive oil."),
        ("Caesar Salad", DishType.Appetizer, 9.00m, "280 g", "410 kcal", "Romaine, grilled chicken, parmesan and crispy croutons."),
        ("Bruschetta Trio", DishType.Appetizer, 7.00m, "200 g", "290 kcal", "Toasted bread with tomato, mushroom and avocado toppings."),
        ("Tom Yum Soup", DishType.Appetizer, 10.50m, "350 g", "260 kcal", "Spicy and sour Thai soup with shrimps and mushrooms."),
        ("Grilled Salmon", DishType.MainCourse, 18.00m, "320 g", "540 kcal", "Salmon fillet with lemon butter, asparagus and baby potatoes."),
        ("Chicken Teriyaki Bowl", DishType.MainCourse, 13.50m, "400 g", "620 kcal", "Rice, glazed chicken, edamame, carrot and sesame."),
        ("Beef Burger", DishType.MainCourse, 14.00m, "380 g", "780 kcal", "Beef patty, cheddar, pickles and house sauce with fries."),
        ("Vegetable Risotto", DishType.MainCourse, 12.00m, "330 g", "480 kcal", "Creamy arborio rice with seasonal vegetables and parmesan."),
        ("Cheesecake", DishType.Dessert, 6.50m, "150 g", "390 kcal", "Classic New York cheesecake with berry sauce."),
        ("Chocolate Lava Cake", DishType.Dessert, 7.50m, "160 g", "450 kcal", "Warm chocolate cake with a molten centre and vanilla ice cream."),
        ("Fruit Sorbet", DishType.Dessert, 5.00m, "120 g", "160 kcal", "Three scoops of mango, raspberry and lemon sorbet."),
    ];

    private static readonly int[] TableCapacities = [2, 2, 4, 4, 6, 8];

    public async Task SeedAsync(CancellationToken cancellationToken)
    {
        using var client = AwsClientFactory.CreateDynamoDb();
        var context = new DynamoDBContextBuilder().WithDynamoDBClient(() => client).Build();

        var existing = await context.LoadAsync<Location>(Locations[0].Id, DynamoConfig.LoadConfig(ResourceNames.LocationsTableName), cancellationToken);
        if (existing is not null)
        {
            logger.LogInformation("Seed data already present, skipping");
            return;
        }

        foreach (var user in Users)
        {
            await CallAsync("POST", "/register", new
            {
                firstName = user.FirstName,
                lastName = user.LastName,
                email = user.Email,
                password = DefaultPassword,
                confirmPassword = DefaultPassword,
            });

            if (user.Group is not null)
                cognito.AddUserToGroup(user.Email, user.Group);
        }

        for (var l = 0; l < Locations.Length; l++)
        {
            var loc = Locations[l];
            var dishIds = new List<string>();

            for (var d = 0; d < Menu.Length; d++)
            {
                var item = Menu[d];
                var dishId = $"{loc.Id}-dish-{d + 1}";
                dishIds.Add(dishId);

                await context.SaveAsync(new Dish
                {
                    Id = dishId,
                    Name = item.Name,
                    Description = item.Description,
                    DishType = item.Type,
                    Price = item.Price,
                    Weight = item.Weight,
                    Calories = item.Calories,
                    Proteins = $"{12 + d} g",
                    Fats = $"{8 + d} g",
                    Carbohydrates = $"{20 + 2 * d} g",
                    Vitamins = "A, B6, C",
                    ImageUrl = $"https://picsum.photos/seed/{Uri.EscapeDataString(item.Name.Replace(' ', '-').ToLowerInvariant())}/600/400",
                    State = l == 2 && d == 3 ? DishState.OutOfStock : DishState.Available,
                    LocationId = loc.Id,
                }, DynamoConfig.SaveConfig(ResourceNames.DishesTableName), cancellationToken);
            }

            for (var t = 0; t < TableCapacities.Length; t++)
            {
                await context.SaveAsync(new Table
                {
                    Id = $"{loc.Id}-table-{t + 1}",
                    LocationId = loc.Id,
                    TableNumber = t + 1,
                    Capacity = TableCapacities[t],
                }, DynamoConfig.SaveConfig(ResourceNames.TablesTableName), cancellationToken);
            }

            await context.SaveAsync(new Location
            {
                Id = loc.Id,
                Address = loc.Address,
                Description = loc.Description,
                TotalCapacity = TableCapacities.Sum(),
                AverageOccupancy = loc.Occupancy,
                ImageUrl = $"https://picsum.photos/seed/restaurant-{loc.Id}/800/500",
                Rating = loc.Rating,
                SpecialtyDishIds = [dishIds[0], dishIds[4], dishIds[9]],
            }, DynamoConfig.SaveConfig(ResourceNames.LocationsTableName), cancellationToken);

            await CallAsync("POST", "/waiters", new { email = $"waiter{l + 1}@example.com", locationId = loc.Id });
        }

        logger.LogInformation("Seed data created: {Users} users, {Locations} locations", Users.Length, Locations.Length);
    }

    private async Task CallAsync(string method, string path, object body)
    {
        var response = await bridge.InvokeAsync(new APIGatewayProxyRequest
        {
            HttpMethod = method,
            Path = path,
            Body = JsonSerializer.Serialize(body),
            Headers = new Dictionary<string, string> { ["Content-Type"] = "application/json" },
            RequestContext = new APIGatewayProxyRequest.ProxyRequestContext { HttpMethod = method, Path = path },
        });

        if (response.StatusCode >= 300)
            logger.LogWarning("Seed call {Method} {Path} returned {Status}: {Body}", method, path, response.StatusCode, response.Body);
    }
}
