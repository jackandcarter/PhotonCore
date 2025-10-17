// SPDX-License-Identifier: Apache-2.0
using Xunit;

namespace PhotonCore.Tests;

public class AuthVerifyTests
{
    [Fact]
    public void BCrypt_Verify_Succeeds_ForMatchingPassword()
    {
        const string password = "s3cret!";
        var hash = BCrypt.Net.BCrypt.HashPassword(password);

        Assert.True(BCrypt.Net.BCrypt.Verify(password, hash));
        Assert.False(BCrypt.Net.BCrypt.Verify("wrong", hash));
    }
}
