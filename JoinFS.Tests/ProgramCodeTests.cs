namespace JoinFS.Tests;

/// <summary>
/// Tests for the Program.Code(...) method which provides encoding/decoding functionality.
/// The Code method is a bidirectional cipher that uses Caesar substitution and random number generation.
/// </summary>
public class ProgramCodeTests
{
    // Standard test key for consistency
    private const int TestKey = 1234;
    
    #region Null and Empty String Tests
    
    [Fact]
    public void Code_WithNullString_ReturnsNull()
    {
        // Arrange
        string? input = null;
        
        // Act
        string? encodedResult = ProgramCode.Code(input, true, TestKey);
        string? decodedResult = ProgramCode.Code(input, false, TestKey);
        
        // Assert
        Assert.Null(encodedResult);
        Assert.Null(decodedResult);
    }
    
    [Fact]
    public void Code_WithEmptyString_ReturnsEmptyString()
    {
        // Arrange
        string input = string.Empty;
        
        // Act
        string encodedResult = ProgramCode.Code(input, true, TestKey);
        string decodedResult = ProgramCode.Code(input, false, TestKey);
        
        // Assert
        Assert.Equal(string.Empty, encodedResult);
        Assert.Equal(string.Empty, decodedResult);
    }
    
    #endregion
    
    #region Round-Trip Tests
    
    [Theory]
    [InlineData("Hello World")]
    [InlineData("Test123")]
    [InlineData("https://example.com")]
    [InlineData("user@email.com")]
    [InlineData("Path/To/File.txt")]
    [InlineData("!@#$%^&*()")]
    [InlineData("ABC")]
    [InlineData("a")]
    public void Code_RoundTrip_RestoresOriginalString(string original)
    {
        // Act
        string encoded = ProgramCode.Code(original, true, TestKey);
        string decoded = ProgramCode.Code(encoded, false, TestKey);
        
        // Assert
        Assert.NotEqual(original, encoded); // Encoded should be different
        Assert.Equal(original, decoded); // Decoded should match original
    }
    
    [Fact]
    public void Code_RoundTrip_WithAllPrintableAsciiCharacters()
    {
        // Arrange - Build a string with all printable ASCII characters (! to ~)
        var sb = new System.Text.StringBuilder();
        for (char c = '!'; c <= '~'; c++)
        {
            sb.Append(c);
        }
        string original = sb.ToString();
        
        // Act
        string encoded = ProgramCode.Code(original, true, TestKey);
        string decoded = ProgramCode.Code(encoded, false, TestKey);
        
        // Assert
        Assert.NotEqual(original, encoded);
        Assert.Equal(original, decoded);
    }
    
    #endregion
    
    #region Key Sensitivity Tests
    
    [Fact]
    public void Code_DifferentKeys_ProduceDifferentEncodings()
    {
        // Arrange
        string input = "Test String";
        int key1 = 1234;
        int key2 = 5678;
        
        // Act
        string encoded1 = ProgramCode.Code(input, true, key1);
        string encoded2 = ProgramCode.Code(input, true, key2);
        
        // Assert
        Assert.NotEqual(encoded1, encoded2);
    }
    
    [Fact]
    public void Code_WrongKey_DoesNotRestoreOriginal()
    {
        // Arrange
        string input = "Secret Message";
        int encodeKey = 1234;
        int wrongKey = 5678;
        
        // Act
        string encoded = ProgramCode.Code(input, true, encodeKey);
        string decodedWithWrongKey = ProgramCode.Code(encoded, false, wrongKey);
        
        // Assert
        Assert.NotEqual(input, decodedWithWrongKey);
    }
    
    [Fact]
    public void Code_SameKeyUsedTwice_RestoresOriginal()
    {
        // Arrange
        string input = "Consistent Key Test";
        int key = 9999;
        
        // Act
        string encoded = ProgramCode.Code(input, true, key);
        string decoded = ProgramCode.Code(encoded, false, key);
        
        // Assert
        Assert.Equal(input, decoded);
    }
    
    #endregion
    
    #region Special Character Tests
    
    [Theory]
    [InlineData("!")]
    [InlineData("~")]
    [InlineData("!~")]
    [InlineData("!!!")]
    [InlineData("~~~")]
    public void Code_WithBoundaryCharacters_RoundTripsCorrectly(string input)
    {
        // Act
        string encoded = ProgramCode.Code(input, true, TestKey);
        string decoded = ProgramCode.Code(encoded, false, TestKey);
        
        // Assert
        Assert.Equal(input, decoded);
    }
    
    [Fact]
    public void Code_WithMixedSpecialCharacters_RoundTripsCorrectly()
    {
        // Arrange
        string input = "Hello!@#$%^&*()_+-=[]{}|;':\",./<>?`~World";
        
        // Act
        string encoded = ProgramCode.Code(input, true, TestKey);
        string decoded = ProgramCode.Code(encoded, false, TestKey);
        
        // Assert
        Assert.Equal(input, decoded);
    }
    
    #endregion
    
    #region Edge Cases with Invalid Characters
    
    [Fact]
    public void Code_WithOnlyInvalidCharacters_ReturnsEmptyString()
    {
        // Arrange - Characters outside the valid range (! to ~)
        string input = "\t\n\r";
        
        // Act
        string encoded = ProgramCode.Code(input, true, TestKey);
        
        // Assert
        Assert.Equal(string.Empty, encoded);
    }
    
    [Fact]
    public void Code_WithInvalidCharactersAtBoundaries_TrimsCorrectly()
    {
        // Arrange - Invalid chars at start and end
        string input = "\t\nHello World\r\n";
        string expectedTrimmed = "Hello World";
        
        // Act
        string encoded = ProgramCode.Code(input, true, TestKey);
        string decoded = ProgramCode.Code(encoded, false, TestKey);
        
        // Assert
        Assert.Equal(expectedTrimmed, decoded);
    }
    
    #endregion
    
    #region Encoding/Decoding Properties
    
    [Fact]
    public void Code_Encoding_ProducesDifferentOutput()
    {
        // Arrange
        string input = "This should be different";
        
        // Act
        string encoded = ProgramCode.Code(input, true, TestKey);
        
        // Assert
        Assert.NotEqual(input, encoded);
        Assert.NotEmpty(encoded);
    }
    
    [Fact]
    public void Code_EncodingSameStringTwice_ProducesSameResult()
    {
        // Arrange
        string input = "Deterministic Test";
        
        // Act
        string encoded1 = ProgramCode.Code(input, true, TestKey);
        string encoded2 = ProgramCode.Code(input, true, TestKey);
        
        // Assert
        Assert.Equal(encoded1, encoded2);
    }
    
    [Fact]
    public void Code_EncodedLength_MatchesValidCharacterCount()
    {
        // Arrange
        string input = "Test";
        
        // Act
        string encoded = ProgramCode.Code(input, true, TestKey);
        
        // Assert
        // The encoded string should have the same length as the original
        // (assuming all characters are valid)
        Assert.Equal(input.Length, encoded.Length);
    }
    
    #endregion
    
    #region Multiple Round-Trips
    
    [Fact]
    public void Code_MultipleRoundTrips_MaintainsIntegrity()
    {
        // Arrange
        string original = "Multiple Round Trip Test";
        
        // Act & Assert - Perform 5 round trips
        string current = original;
        for (int i = 0; i < 5; i++)
        {
            string encoded = ProgramCode.Code(current, true, TestKey);
            string decoded = ProgramCode.Code(encoded, false, TestKey);
            Assert.Equal(current, decoded);
            current = decoded;
        }
        
        Assert.Equal(original, current);
    }
    
    #endregion
    
    #region Real-World Usage Scenarios
    
    [Fact]
    public void Code_WithUrl_RoundTripsCorrectly()
    {
        // Arrange
        string url = "https://joinfs.net/download/file.zip";
        
        // Act
        string encoded = ProgramCode.Code(url, true, TestKey);
        string decoded = ProgramCode.Code(encoded, false, TestKey);
        
        // Assert
        Assert.Equal(url, decoded);
    }
    
    [Fact]
    public void Code_WithFilePath_RoundTripsCorrectly()
    {
        // Arrange
        string path = "C:/Program Files/JoinFS/config.xml";
        
        // Act
        string encoded = ProgramCode.Code(path, true, TestKey);
        string decoded = ProgramCode.Code(encoded, false, TestKey);
        
        // Assert
        Assert.Equal(path, decoded);
    }
    
    [Fact]
    public void Code_WithLongString_RoundTripsCorrectly()
    {
        // Arrange
        string longString = new string('A', 1000) + " test " + new string('Z', 1000);
        
        // Act
        string encoded = ProgramCode.Code(longString, true, TestKey);
        string decoded = ProgramCode.Code(encoded, false, TestKey);
        
        // Assert
        Assert.Equal(longString, decoded);
    }
    
    #endregion
    
    #region Key Value Tests
    
    [Theory]
    [InlineData(0)]
    [InlineData(1)]
    [InlineData(-1)]
    [InlineData(int.MaxValue)]
    [InlineData(int.MinValue)]
    public void Code_WithVariousKeys_RoundTripsCorrectly(int key)
    {
        // Arrange
        string input = "Key variation test";
        
        // Act
        string encoded = ProgramCode.Code(input, true, key);
        string decoded = ProgramCode.Code(encoded, false, key);
        
        // Assert
        Assert.Equal(input, decoded);
    }
    
    #endregion
}
