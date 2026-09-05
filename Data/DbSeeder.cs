using FridgeManager.Data.Entities;
using FridgeManager.Data.Enums;
using Microsoft.AspNetCore.Identity;
using Microsoft.EntityFrameworkCore;

namespace FridgeManager.Data;

public static class DbSeeder
{
    public const string DevPassword = "Passw0rd!";

    public static async Task EnsureRolesAsync(RoleManager<IdentityRole> roles)
    {
        await EnsureRoleAsync(roles, "Admin");
        await EnsureRoleAsync(roles, "User");
    }

    public static async Task SeedDemoDataAsync(IServiceProvider services)
    {
        var db = services.GetRequiredService<AppDbContext>();
        var users = services.GetRequiredService<UserManager<ApplicationUser>>();
        var roles = services.GetRequiredService<RoleManager<IdentityRole>>();

        await EnsureRolesAsync(roles);
        await RemoveLegacyExampleAdminAsync(users, db);

        var admin = await EnsureUserAsync(users, "admin", "admin@fridge.local", 5, "Admin");
        var alice = await EnsureUserAsync(users, "alice", "alice@fridge.local", 10, "User");
        var bob = await EnsureUserAsync(users, "bob", "bob@fridge.local", 8, "User");
        var carol = await EnsureUserAsync(users, "carol", "carol@fridge.local", 3, "User");

        var fridge = await db.Refrigerators.Include(r => r.Shelves).FirstOrDefaultAsync();
        if (fridge is null)
        {
            fridge = new Refrigerator { Name = "Office Fridge" };
            db.Refrigerators.Add(fridge);
            await db.SaveChangesAsync();

            db.Shelves.AddRange(
                new Shelf { RefrigeratorId = fridge.Id, Name = "Shelf A", CapacityUnits = 20, SortOrder = 1 },
                new Shelf { RefrigeratorId = fridge.Id, Name = "Shelf B", CapacityUnits = 20, SortOrder = 2 },
                new Shelf { RefrigeratorId = fridge.Id, Name = "Shelf C", CapacityUnits = 15, SortOrder = 3 },
                new Shelf { RefrigeratorId = fridge.Id, Name = "Shelf D", CapacityUnits = 10, SortOrder = 4 });
            await db.SaveChangesAsync();
        }

        if (await db.FoodItems.AnyAsync())
        {
            return;
        }

        var shelves = await db.Shelves.OrderBy(s => s.SortOrder).ToListAsync();
        var a = shelves[0];
        var b = shelves[1];
        var c = shelves[2];
        var d = shelves[3];

        var today = DateOnly.FromDateTime(DateTime.UtcNow);
        var now = DateTime.UtcNow;

        FoodItem Item(
            string name,
            ApplicationUser owner,
            FoodCategory category,
            DateOnly expiration,
            int size,
            bool shared,
            FoodStatus status,
            Shelf shelf,
            string? position = null,
            string? note = null)
            => new()
            {
                Name = name,
                OwnerId = owner.Id,
                Category = category,
                ExpirationDate = expiration,
                SizeUnits = size,
                IsShared = shared,
                Status = status,
                ShelfId = shelf.Id,
                PositionNote = position,
                Note = note,
                CreatedAt = now,
                UpdatedAt = now
            };

        db.FoodItems.AddRange(
            // Shelf A Active (9 units). Carol owns Green Tea + Protein Bar.
            Item("Milk", alice, FoodCategory.Drink, today.AddDays(-5), 2, false, FoodStatus.Active, a, "Door", "Already sour"),
            Item("Green Tea", carol, FoodCategory.Drink, today.AddDays(14), 1, true, FoodStatus.Active, a),
            Item("Protein Bar", carol, FoodCategory.Snack, today.AddDays(20), 1, false, FoodStatus.Active, a, "Left bin"),
            Item("Sparkling Water", alice, FoodCategory.Drink, today.AddDays(30), 1, true, FoodStatus.Active, a),
            Item("Kimchi", bob, FoodCategory.Ingredient, today.AddDays(10), 2, true, FoodStatus.Active, a, "Bottom"),
            Item("Butter", admin, FoodCategory.Ingredient, today.AddDays(40), 1, true, FoodStatus.Active, a),
            Item("Baking Soda", admin, FoodCategory.Other, today.AddDays(365), 1, true, FoodStatus.Active, a, "Back corner"),

            // Shelf B Active (10 units). Carol owns Salad Box (quota = 3).
            Item("Salad Box", carol, FoodCategory.Meal, today.AddDays(1), 2, true, FoodStatus.Active, b, "Front"),
            Item("Eggs", bob, FoodCategory.Ingredient, today.AddDays(1), 2, true, FoodStatus.Active, b),
            Item("Orange Juice", alice, FoodCategory.Drink, today.AddDays(2), 2, true, FoodStatus.Active, b),
            Item("Chicken Meal", bob, FoodCategory.Meal, today.AddDays(5), 3, false, FoodStatus.Active, b, "Middle"),
            Item("Soda", alice, FoodCategory.Drink, today.AddDays(60), 1, false, FoodStatus.Active, b),

            // Shelf C Active (8 units)
            Item("Shared Cookies", bob, FoodCategory.Snack, today.AddDays(3), 1, true, FoodStatus.Active, c),
            Item("Instant Noodles", alice, FoodCategory.Meal, today.AddDays(90), 2, false, FoodStatus.Active, c),
            Item("Frozen Dumplings", bob, FoodCategory.Meal, today.AddDays(45), 3, true, FoodStatus.Active, c, "Back"),
            Item("Soy Sauce", admin, FoodCategory.Ingredient, today.AddDays(180), 1, true, FoodStatus.Active, c),
            Item("Energy Drink", alice, FoodCategory.Drink, today.AddDays(7), 1, false, FoodStatus.Active, c),

            // Shelf D Active (9 / 10 units) — ≥ 90% full
            Item("Yogurt Drink", alice, FoodCategory.Drink, today, 2, true, FoodStatus.Active, d, "Left"),
            Item("Leftover Pasta", bob, FoodCategory.Meal, today.AddDays(-2), 3, false, FoodStatus.Active, d, "Right"),
            Item("Cheese Block", admin, FoodCategory.Ingredient, today.AddDays(14), 2, true, FoodStatus.Active, d),
            Item("Mystery Sauce", alice, FoodCategory.Other, today.AddDays(21), 2, false, FoodStatus.Active, d, note: "Unlabeled jar"),

            // Non-active items (do not consume capacity)
            Item("Banana Bread", bob, FoodCategory.Snack, today.AddDays(3), 2, true, FoodStatus.Consumed, a),
            Item("Pizza Slice", alice, FoodCategory.Meal, today.AddDays(1), 2, false, FoodStatus.Consumed, b),
            Item("Takeout Box", bob, FoodCategory.Meal, today.AddDays(2), 2, false, FoodStatus.Consumed, c),
            Item("Kombucha", bob, FoodCategory.Drink, today.AddDays(10), 1, true, FoodStatus.Missing, c),
            Item("Tofu", alice, FoodCategory.Ingredient, today.AddDays(4), 1, false, FoodStatus.Missing, a),
            Item("Gummy Bears", alice, FoodCategory.Snack, today.AddDays(15), 1, true, FoodStatus.Missing, c),
            Item("Party Cake", bob, FoodCategory.Other, today.AddDays(-1), 3, true, FoodStatus.Discarded, b),
            Item("Old Sandwich", bob, FoodCategory.Meal, today.AddDays(-10), 2, false, FoodStatus.Discarded, a),
            Item("Expired Yogurt", alice, FoodCategory.Drink, today.AddDays(-7), 1, false, FoodStatus.Discarded, c));

        await db.SaveChangesAsync();
    }

    private static async Task EnsureRoleAsync(RoleManager<IdentityRole> roles, string name)
    {
        if (!await roles.RoleExistsAsync(name))
        {
            var result = await roles.CreateAsync(new IdentityRole(name));
            if (!result.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create role '{name}': {FormatErrors(result)}");
            }
        }
    }

    private static async Task RemoveLegacyExampleAdminAsync(
        UserManager<ApplicationUser> users,
        AppDbContext db)
    {
        var leftover = await users.FindByEmailAsync("admin@example.com");
        if (leftover is null)
        {
            return;
        }

        if (await db.FoodItems.AnyAsync(f => f.OwnerId == leftover.Id))
        {
            return;
        }

        var deleted = await users.DeleteAsync(leftover);
        if (!deleted.Succeeded)
        {
            throw new InvalidOperationException(
                $"Failed to remove leftover admin 'admin@example.com': {FormatErrors(deleted)}");
        }
    }

    private static async Task<ApplicationUser> EnsureUserAsync(
        UserManager<ApplicationUser> users,
        string userName,
        string email,
        int quota,
        string role)
    {
        var user = await users.FindByEmailAsync(email);
        if (user is null)
        {
            user = new ApplicationUser
            {
                UserName = userName,
                Email = email,
                EmailConfirmed = true,
                ItemQuota = quota,
                IsActive = true
            };
            var created = await users.CreateAsync(user, DevPassword);
            if (!created.Succeeded)
            {
                throw new InvalidOperationException($"Failed to create user '{email}': {FormatErrors(created)}");
            }
        }
        else
        {
            if (!string.Equals(user.UserName, userName, StringComparison.Ordinal))
            {
                var occupant = await users.FindByNameAsync(userName);
                if (occupant is null || occupant.Id == user.Id)
                {
                    var renamed = await users.SetUserNameAsync(user, userName);
                    if (!renamed.Succeeded)
                    {
                        throw new InvalidOperationException(
                            $"Failed to set username '{userName}' for '{email}': {FormatErrors(renamed)}");
                    }
                }
            }

            if (user.ItemQuota != quota)
            {
                user.ItemQuota = quota;
                var updated = await users.UpdateAsync(user);
                if (!updated.Succeeded)
                {
                    throw new InvalidOperationException(
                        $"Failed to set quota for '{email}': {FormatErrors(updated)}");
                }
            }
        }

        if (!await users.IsInRoleAsync(user, role))
        {
            var added = await users.AddToRoleAsync(user, role);
            if (!added.Succeeded)
            {
                throw new InvalidOperationException($"Failed to add '{email}' to role '{role}': {FormatErrors(added)}");
            }
        }

        return user;
    }

    private static string FormatErrors(IdentityResult result)
        => string.Join("; ", result.Errors.Select(e => e.Description));
}
