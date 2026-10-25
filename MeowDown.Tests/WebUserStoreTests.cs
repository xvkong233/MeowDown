using System;
using System.Linq;

namespace MeowDown.Tests;

public class WebUserStoreTests : IDisposable
{
    private readonly WebTestDirectory files = new();
    private string StorePath => files.PathFor("web-users.json");

    public void Dispose() => files.Dispose();

    [Fact]
    public void BootstrapAdmin_CreatesAdminInPasswordChangeRequiredState()
    {
        var store = CreateStore();

        var (user, password) = store.BootstrapAdmin("initial-pw");

        Assert.Equal("admin", user.Username);
        Assert.Equal(WebUserRole.Admin, user.Role);
        Assert.True(user.PasswordChangeRequired);
        Assert.StartsWith("u_", user.Id);
        Assert.True(WebPasswordHasher.Verify("initial-pw", user.PasswordHash));
        Assert.False(store.IsEmpty);
    }

    [Fact]
    public void BootstrapAdmin_Generates16CharacterAlphanumericPasswordWhenMissing()
    {
        var store = CreateStore();

        var (_, password) = store.BootstrapAdmin(null);

        Assert.True(WebUserStore.ValidatePassword(password) is null);
        Assert.Equal(16, password.Length);
        Assert.All(password, character => Assert.True(char.IsAsciiLetterOrDigit(character)));
    }

    [Fact]
    public void BootstrapAdmin_ThrowsWhenUserStoreNotEmpty()
    {
        var store = CreateStore();
        store.BootstrapAdmin("initial-pw");

        Assert.Throws<InvalidOperationException>(() => store.BootstrapAdmin("another"));
    }

    [Fact]
    public void Create_PersistsAcrossStoreInstancesWithCaseInsensitiveLookup()
    {
        var store = CreateStore();
        var user = store.Create("alice", "password-1", WebUserRole.User);
        Assert.NotNull(user);

        var reloaded = CreateStore();
        Assert.Equal("alice", reloaded.FindByUsername("ALICE")?.Username);
        Assert.Null(reloaded.FindByUsername("bob"));
    }

    [Fact]
    public void Create_ReturnsNullForDuplicateUsernameRegardlessOfCase()
    {
        var store = CreateStore();
        Assert.NotNull(store.Create("alice", "password-1", WebUserRole.User));

        Assert.Null(store.Create("Alice", "password-2", WebUserRole.User));
        Assert.Single(store.All(), user => user.Username.Equals("alice", StringComparison.OrdinalIgnoreCase));
    }

    [Fact]
    public void Create_SupportsAdminRoleAndChangeRequiredFlag()
    {
        var store = CreateStore();

        var user = store.Create("bob", "password-2", WebUserRole.Admin, passwordChangeRequired: true);

        Assert.Equal(WebUserRole.Admin, user!.Role);
        Assert.True(user.PasswordChangeRequired);
    }

    [Fact]
    public void Remove_DeletesUserAndPersists()
    {
        var store = CreateStore();
        var user = store.Create("carol", "password-3", WebUserRole.User)!;

        Assert.True(store.Remove(user.Id));
        Assert.False(store.Remove(user.Id)); // 幂等
        Assert.Null(store.FindById(user.Id));
        Assert.Null(CreateStore().FindById(user.Id));
    }

    [Fact]
    public void UpdatePasswordHash_CanMarkAndClearChangeRequired()
    {
        var store = CreateStore();
        var user = store.Create("dave", "password-4", WebUserRole.User)!;

        store.UpdatePasswordHash(user, WebPasswordHasher.Hash("new-password", iterations: 1000), markChangeRequired: true);
        Assert.True(store.FindById(user.Id)!.PasswordChangeRequired);
        Assert.True(WebPasswordHasher.Verify("new-password", store.FindById(user.Id)!.PasswordHash));

        store.UpdatePasswordHash(store.FindById(user.Id)!, WebPasswordHasher.Hash("another-pass", iterations: 1000), clearChangeRequired: true);
        Assert.False(store.FindById(user.Id)!.PasswordChangeRequired);
    }

    [Fact]
    public void CorruptedUserFile_FailsLoudlyInsteadOfResettingAccounts()
    {
        System.IO.File.WriteAllText(StorePath, "{ not json");

        Assert.Throws<InvalidOperationException>(() => CreateStore());
    }

    [Theory]
    [InlineData("ab", false)]
    [InlineData("abc", true)]
    [InlineData("abbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", true)] // 32 位
    [InlineData("abbbbbbbbbbbbbbbbbbbbbbbbbbbbbbbb", false)] // 33 位
    [InlineData("bad name", false)]
    [InlineData("bad.name", false)]
    [InlineData("ok_name-1", true)]
    [InlineData("中文名", false)]
    [InlineData(null, false)]
    [InlineData("", false)]
    public void ValidateUsername_EnforcesContractPattern(string? username, bool valid)
    {
        var error = WebUserStore.ValidateUsername(username);

        Assert.Equal(valid, error is null);
    }

    [Theory]
    [InlineData(7, false)]
    [InlineData(8, true)]
    [InlineData(128, true)]
    [InlineData(129, false)]
    [InlineData(0, false)] // 0 表示 null
    [InlineData(-1, false)] // 负数表示空串
    public void ValidatePassword_EnforcesLength8To128(int length, bool valid)
    {
        var password = length == 0 ? null : length < 0 ? "" : new string('p', length);

        Assert.Equal(valid, WebUserStore.ValidatePassword(password) is null);
    }

    private WebUserStore CreateStore()
        => new(StorePath,
            hashPassword: password => WebPasswordHasher.Hash(password, iterations: 1000),
            passwordGenerator: () => "Fixed16PasswordX");
}
