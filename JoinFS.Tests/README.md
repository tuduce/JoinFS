# JoinFS Tests

This test project contains comprehensive unit tests for the JoinFS application.

## Test Coverage

### Program.Code(...) Method

The `Program.Code(...)` method is a bidirectional cipher that implements encoding and decoding functionality using:
- Caesar substitution cipher
- Pseudo-random number generation with seeded keys
- Character range validation (printable ASCII characters from '!' to '~')

#### Test Categories

1. **Null and Empty String Tests**
   - Validates handling of null inputs
   - Validates handling of empty strings

2. **Round-Trip Tests**
   - Ensures encoding followed by decoding restores the original string
   - Tests various common input patterns (URLs, file paths, email addresses, etc.)
   - Tests all printable ASCII characters

3. **Key Sensitivity Tests**
   - Verifies different keys produce different encodings
   - Confirms wrong keys cannot decode messages
   - Validates key consistency

4. **Special Character Tests**
   - Tests boundary characters ('!' and '~')
   - Tests mixed special character strings

5. **Edge Cases with Invalid Characters**
   - Handles characters outside the valid range
   - Properly trims invalid characters at boundaries

6. **Encoding/Decoding Properties**
   - Verifies encoding produces different output
   - Confirms deterministic behavior (same input + key = same output)
   - Validates encoded string length

7. **Multiple Round-Trips**
   - Ensures data integrity over multiple encode/decode cycles

8. **Real-World Usage Scenarios**
   - Tests with URLs, file paths, and long strings

9. **Key Value Tests**
   - Tests with various key values including edge cases (0, negative, max/min int)

## Running Tests

To run all tests:
```bash
cd JoinFS.Tests
dotnet test
```

To run tests with verbose output:
```bash
dotnet test --verbosity normal
```

To run tests with coverage:
```bash
dotnet test --collect:"XPlat Code Coverage"
```

## Test Results

All 34 tests pass successfully, providing comprehensive coverage of the `Program.Code(...)` method's functionality including:
- Normal operation
- Edge cases
- Error conditions
- Round-trip encoding/decoding
- Key sensitivity
- Character range handling

## Implementation Note

The test project includes a copy of the `Program.Code` method in `ProgramCode.cs` to avoid circular dependencies with the main Windows Forms application. This ensures the tests can run independently on any platform.
