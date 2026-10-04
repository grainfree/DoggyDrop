using System.Net;
using DoggyDrop.Services;
using Microsoft.EntityFrameworkCore;
using Xunit;
namespace DoggyDrop.Tests;
public sealed partial class IdentityAdversarialTests
{
    private sealed class PrivacyNoMedia : IUserMediaCleanup { public Task CleanupAsync(IEnumerable<string?> urls)=>Task.CompletedTask; }
    [Theory][InlineData("alice")][InlineData("external")]
    public async Task OwnerDeletionWithActualIdentityCookieSignsOutAndCannotChooseAnotherUser(string id)
    {
        const string path="/Identity/Account/Manage/DeletePersonalData";
        using var client=await SignedIn(id);
        var form=new Dictionary<string,string>{{"Input.ConfirmDeletion","true"},{"userId","bob"},{"Input.Password",id=="alice"?initial:""}};
        Assert.Equal(HttpStatusCode.BadRequest,(await Form(client,path,new(form),token:false)).StatusCode);
        var result=await Form(client,path+"?userId=bob",form);
        Assert.Equal(HttpStatusCode.Redirect,result.StatusCode);
        Assert.Equal(HttpStatusCode.Redirect,(await client.GetAsync(path)).StatusCode);
        await With(async(db,users)=>{Assert.False(await db.Users.AnyAsync(u=>u.Id==id));Assert.True(await db.Users.AnyAsync(u=>u.Id=="bob"));});
    }
}
