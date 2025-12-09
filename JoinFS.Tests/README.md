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

### EnrichModelService Class

The `EnrichModelService` class handles enrichment of aircraft model data through API calls and local caching.

#### Test Categories

1. **Constructor Tests**
   - Validates initialization with valid and invalid file paths
   - Tests loading of existing data from JSONL files
   - Handles malformed JSON lines gracefully
   - Skips entries with null or empty titles

2. **QueryAndStoreModelDetailsAsync Tests**
   - Verifies API is not called for empty lists
   - Skips already loaded models to avoid redundant API calls
   - Correctly queries API for new models
   - Batches large requests (20 models per batch)
   - Saves enriched data to JSONL file
   - Handles duplicate titles correctly

3. **EnrichModelsWithDetailsAsync Tests**
   - Enriches multiple models in a single operation
   - Handles null models gracefully
   - Handles empty or whitespace titles
   - Uses distinct titles to minimize API calls

4. **EnrichModel Tests**
   - Synchronous enrichment of single models
   - Null-safety for null models and empty titles
   - Uses cached data when available

### EmbeddingService Class (X64 only)

The `EmbeddingService` class provides BERT-based text embeddings for semantic similarity calculations. These tests are only compiled and run on x64 platforms.

#### Test Categories

1. **Normalize Tests**
   - Normalizes vectors to unit length
   - Handles unit vectors correctly
   - Gracefully handles zero vectors
   - Verifies normalized vectors have magnitude 1.0

2. **MeanPooling Tests**
   - Calculates correct mean pooling over token embeddings
   - Respects attention mask to skip padding tokens
   - Handles cases with all valid tokens
   - Handles single valid token cases

3. **CosineSimilarity Tests**
   - Returns 1.0 for identical vectors
   - Returns 0.0 for orthogonal vectors
   - Returns -1.0 for opposite vectors
   - Handles scaled vectors correctly
   - Validates that similarity is always in range [-1, 1]
   - Throws exception for different length vectors

4. **FindBestMatchingModel Tests**
   - Returns null when no match exceeds threshold
   - Correctly identifies the best matching model
   - Skips models without embeddings

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

All 53 tests pass successfully, providing comprehensive coverage of:
- **ProgramCode**: 34 tests covering encoding/decoding functionality
- **EnrichModelService**: 16 tests covering model enrichment with API integration
- **EmbeddingService**: 3 test classes covering mathematical operations (x64 only)

The test suite covers:
- Normal operation
- Edge cases
- Error conditions
- Null safety
- Round-trip operations
- Mock HTTP client interactions
- File I/O operations

## Implementation Note

The test project includes standalone copies of the classes being tested to avoid circular dependencies with the main Windows Forms application. This ensures the tests can run independently on any platform (including non-Windows CI/CD environments).

## Platform Notes

- Most tests run on all platforms (x86 and x64)
- EmbeddingService tests only run on x64 due to ML.OnnxRuntime platform requirements
- The test project uses conditional compilation (`#if X64`) to handle platform-specific code
