using Microsoft.EntityFrameworkCore;

namespace Fabulis.Server.Data;

public class DraftService(FabulisDbContext db)
{
    public async Task<Draft> CreateDraftAsync(int storytellerId)
    {
        var draft = new Draft
        {
            StorytellerID = storytellerId,
            CreatedAt = DateTime.UtcNow,
            UpdatedAt = DateTime.UtcNow
        };
        db.Drafts.Add(draft);
        await db.SaveChangesAsync();
        return draft;
    }

    public async Task<List<Draft>> GetDraftsAsync()
    {
        return await db.Drafts
            .Include(d => d.Storyteller)
            .Include(d => d.Messages)
            .OrderByDescending(d => d.UpdatedAt)
            .ToListAsync();
    }

    public async Task<Draft?> GetDraftAsync(int id)
    {
        return await db.Drafts
            .Include(d => d.Storyteller)
            .Include(d => d.Messages.OrderBy(m => m.SortOrder))
            .FirstOrDefaultAsync(d => d.Id == id);
    }

    public async Task<DraftMessage> AddMessageAsync(int draftId, MessageRole role, string content)
    {
        var draft = await db.Drafts.Include(d => d.Messages).FirstAsync(d => d.Id == draftId);
        var nextOrder = draft.Messages.Count > 0 ? draft.Messages.Max(m => m.SortOrder) + 1 : 0;

        var message = new DraftMessage
        {
            DraftId = draftId,
            Role = role,
            Content = content,
            SortOrder = nextOrder
        };
        db.DraftMessages.Add(message);

        draft.UpdatedAt = DateTime.UtcNow;
        if (draft.Title is null && role == MessageRole.Prompt)
        {
            draft.Title = content.Length > 60 ? content[..60] + "..." : content;
        }

        await db.SaveChangesAsync();
        return message;
    }

    public async Task UpdateDraftTitleAsync(int draftId, string title)
    {
        var draft = await db.Drafts.FindAsync(draftId);
        if (draft is not null)
        {
            draft.Title = title;
            draft.UpdatedAt = DateTime.UtcNow;
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteDraftAsync(int draftId)
    {
        var draft = await db.Drafts.Include(d => d.Messages).FirstOrDefaultAsync(d => d.Id == draftId);
        if (draft is not null)
        {
            db.DraftMessages.RemoveRange(draft.Messages);
            db.Drafts.Remove(draft);
            await db.SaveChangesAsync();
        }
    }

    public async Task DeleteMessageAndSubsequentAsync(int messageId)
    {
        var message = await db.DraftMessages.FindAsync(messageId);
        if (message is null) return;

        var subsequent = await db.DraftMessages
            .Where(m => m.DraftId == message.DraftId && m.SortOrder >= message.SortOrder)
            .ToListAsync();

        db.DraftMessages.RemoveRange(subsequent);

        var draft = await db.Drafts.FindAsync(message.DraftId);
        if (draft is not null) draft.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task UpdateMessageContentAsync(int messageId, string content)
    {
        var message = await db.DraftMessages.FindAsync(messageId);
        if (message is null) return;

        message.Content = content;

        var draft = await db.Drafts.FindAsync(message.DraftId);
        if (draft is not null) draft.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task UpdateMessageAndDeleteSubsequentAsync(int messageId, string content)
    {
        var message = await db.DraftMessages.FindAsync(messageId);
        if (message is null) return;

        message.Content = content;

        var subsequent = await db.DraftMessages
            .Where(m => m.DraftId == message.DraftId && m.SortOrder > message.SortOrder)
            .ToListAsync();

        db.DraftMessages.RemoveRange(subsequent);

        var draft = await db.Drafts.FindAsync(message.DraftId);
        if (draft is not null) draft.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
    }

    public async Task<bool> DeleteLastResponseAsync(int draftId)
    {
        var lastMessage = await db.DraftMessages
            .Where(m => m.DraftId == draftId)
            .OrderByDescending(m => m.SortOrder)
            .FirstOrDefaultAsync();

        if (lastMessage is null || lastMessage.Role != MessageRole.Response)
            return false;

        db.DraftMessages.Remove(lastMessage);

        var draft = await db.Drafts.FindAsync(draftId);
        if (draft is not null) draft.UpdatedAt = DateTime.UtcNow;

        await db.SaveChangesAsync();
        return true;
    }

    public async Task<StoryVersion> SaveToLibraryAsync(int draftId, int categoryId, int? storyId, string? newStoryTitle)
    {
        var draft = await db.Drafts
            .Include(d => d.Storyteller)
            .Include(d => d.Messages.OrderBy(m => m.SortOrder))
            .FirstAsync(d => d.Id == draftId);

        Story story;
        if (storyId.HasValue)
        {
            story = await db.Stories.Include(s => s.Versions).FirstAsync(s => s.Id == storyId.Value);
        }
        else
        {
            story = new Story
            {
                CategoryId = categoryId,
                Title = newStoryTitle ?? draft.Title ?? "Untitled",
                CreatedAt = DateTime.UtcNow
            };
            db.Stories.Add(story);
            await db.SaveChangesAsync();
        }

        var versionNumber = story.Versions.Count > 0 ? story.Versions.Max(v => v.VersionNumber) + 1 : 1;

        var version = new StoryVersion
        {
            StoryId = story.Id,
            VersionNumber = versionNumber,
            ModelName = draft.Storyteller.ModelName,
            CreatedAt = DateTime.UtcNow
        };
        db.StoryVersions.Add(version);
        await db.SaveChangesAsync();

        foreach (var msg in draft.Messages)
        {
            db.StoryMessages.Add(new StoryMessage
            {
                StoryVersionId = version.Id,
                Role = msg.Role,
                Content = msg.Content,
                SortOrder = msg.SortOrder
            });
        }
        await db.SaveChangesAsync();

        db.DraftMessages.RemoveRange(draft.Messages);
        db.Drafts.Remove(draft);
        await db.SaveChangesAsync();

        return version;
    }
}
