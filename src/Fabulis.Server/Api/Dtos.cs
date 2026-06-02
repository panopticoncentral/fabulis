using Fabulis.Server.Data;

namespace Fabulis.Server.Api;

// ---------- auth ----------
public sealed record UnlockRequest(string Password);
public sealed record UnlockResponse(string Token, DateTime IssuedAt);
public sealed record AuthStatusResponse(bool IsUnlocked, int? AutoLockMinutes);

// ---------- library / categories / stories ----------
public sealed record LibraryResponse(IReadOnlyList<CategorySummaryDto> Categories);

public sealed record CategorySummaryDto(
    int Id,
    string Name,
    DateTime CreatedAt,
    int StoryCount,
    string? LatestStoryTitle);

public sealed record CategoryDto(
    int Id,
    string Name,
    DateTime CreatedAt,
    IReadOnlyList<StorySummaryDto> Stories);

public sealed record StorySummaryDto(
    int Id,
    string Title,
    DateTime CreatedAt,
    int VersionCount);

public sealed record StoryDto(
    int Id,
    int CategoryId,
    string CategoryName,
    string Title,
    DateTime CreatedAt,
    IReadOnlyList<StoryVersionSummaryDto> Versions);

public sealed record StoryVersionSummaryDto(
    int Id,
    int VersionNumber,
    string ModelName,
    DateTime CreatedAt);

public sealed record StoryVersionDto(
    int Id,
    int StoryId,
    int VersionNumber,
    string ModelName,
    DateTime CreatedAt,
    IReadOnlyList<StoryMessageDto> Messages);

public sealed record StoryMessageDto(
    int Id,
    MessageRole Role,
    string Content,
    int SortOrder);

// ---------- settings ----------
public sealed record SettingsDto(
    bool ApiKeyIsSet,
    string? AssistantModel,
    string AutoLockSelection, // "1"/"5"/"15"/"30"/"60"/"never"
    bool KokoroBaseUrlIsSet,
    string? NarrationVoice,
    double NarrationSpeed,
    bool NarrationAvailable);

public sealed record SettingsUpdateRequest(
    string? ApiKey,             // null = leave alone
    string? AssistantModel,     // null = leave alone
    string? AutoLockSelection,  // null = leave alone, otherwise one of the legal strings
    string? KokoroBaseUrl,      // null = leave alone; empty string = clear
    string? NarrationVoice,     // null = leave alone
    double? NarrationSpeed);    // null = leave alone

// ---------- narration ----------
public sealed record NarrationVoiceDto(string Id, string DisplayName, string Language);
public sealed record VoicesResponse(IReadOnlyList<NarrationVoiceDto> Voices);
public sealed record SynthesizeRequest(string Text, string? Voice, double? Speed);
public sealed record PrepareResponse(string Token);

// ---------- storyteller ----------
public sealed record StorytellerDto(
    int Id,
    string Name,
    string Prompt,
    string TitlingPrompt,
    string ModelName,
    double Temperature,
    double? TopP,
    int? MaxTokens,
    double? MinP,
    int? TopK,
    double? TopA);

public sealed record StorytellerUpdateRequest(
    string Name,
    string Prompt,
    string TitlingPrompt,
    string ModelName,
    double Temperature,
    double? TopP,
    int? MaxTokens,
    double? MinP,
    int? TopK,
    double? TopA);

// ---------- drafts ----------
public sealed record DraftSummaryDto(
    int Id,
    string? Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    int MessageCount);

public sealed record DraftDto(
    int Id,
    string? Title,
    DateTime CreatedAt,
    DateTime UpdatedAt,
    string StorytellerName,
    string ModelName,
    IReadOnlyList<DraftMessageDto> Messages);

public sealed record DraftMessageDto(
    int Id,
    MessageRole Role,
    string Content,
    int SortOrder);

public sealed record StreamPromptRequest(string Prompt);

public sealed record StreamEnvelope(
    string Kind,
    string? Text,
    bool? Reasoning,
    int? MessageId);

public sealed record SaveDraftRequest(
    int? CategoryId,
    string? NewCategoryName,
    int? StoryId,
    string? NewStoryTitle);

public sealed record SaveDraftResponse(
    int StoryId,
    int VersionId,
    int VersionNumber);

public sealed record CreateCategoryRequest(string Name);
public sealed record RenameCategoryRequest(string Name);
public sealed record ModelInfoDto(string Id, string Name);
public sealed record UpdateMessageRequest(string Content);
