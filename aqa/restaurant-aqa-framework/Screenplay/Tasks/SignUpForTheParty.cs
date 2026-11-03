using Restaurant_Aqa_Framework.Api;
using Restaurant_Aqa_Framework.Api.Dtos.Auth;
using Restaurant_Aqa_Framework.Api.Seeding;

namespace Restaurant_Aqa_Framework.Screenplay.Tasks;

/// <summary>Registers a fresh customer, signs them in and remembers the account under <paramref name="role"/>.</summary>
public sealed class SignUpForTheParty(string role) : ITask
{
    public async Task PerformAsAsync(Actor actor)
    {
        var visitor = SampleDataFactory.RegisterRequest();
        var api = new AuthApi(actor.UsingTheApi());

        var registered = await api.RegisterAsync(visitor);
        if (!registered.Ok)
            throw new InvalidOperationException(
                $"Registering the {role} failed with {registered.Status}: {await registered.ReadErrorMessageAsync()}");

        var token = await SignInAsync(api, visitor.Email, visitor.Password, role);
        actor.Remember(role, new PartyAccount(visitor.Email, visitor.Password, token));
    }

    internal static async Task<string> SignInAsync(AuthApi api, string email, string password, string role)
    {
        var signedIn = await api.SignInAsync(new SignInRequest(email, password));
        if (!signedIn.Ok)
            throw new InvalidOperationException(
                $"Signing in the {role} failed with {signedIn.Status}: {await signedIn.ReadErrorMessageAsync()}");

        return (await signedIn.ReadAsAsync<SignInResponse>()).IdToken;
    }
}
