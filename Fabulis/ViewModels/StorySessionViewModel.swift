import Foundation
import SwiftData
import SwiftUI

@Observable
final class StorySessionViewModel {
    var currentStory: Story?
    var streamingContent: String = ""
    var isGenerating: Bool = false
    var error: String?
    var userInput: String = ""

    private let openRouter = OpenRouterService.shared

    var displaySegments: [StorySegment] {
        currentStory?.orderedSegments.filter { $0.role != .system } ?? []
    }

    @MainActor
    func startNewStory(
        prompt: String,
        storyteller: Storyteller,
        modelContext: ModelContext
    ) async {
        let story = Story(
            title: generateTitle(from: prompt),
            storyteller: storyteller,
            modelIdSnapshot: storyteller.selectedModelId
        )
        modelContext.insert(story)

        let systemSegment = StorySegment(
            content: storyteller.systemPrompt,
            role: .system,
            orderIndex: 0,
            story: story
        )
        modelContext.insert(systemSegment)

        let userSegment = StorySegment(
            content: prompt,
            role: .user,
            orderIndex: 1,
            story: story
        )
        modelContext.insert(userSegment)

        try? modelContext.save()

        currentStory = story

        await generateNextSegment(modelContext: modelContext)
    }

    @MainActor
    func continueStory(
        direction: String? = nil,
        modelContext: ModelContext
    ) async {
        guard let story = currentStory else { return }

        let userMessage = direction ?? "Continue the story."

        let nextIndex = (story.orderedSegments.last?.orderIndex ?? 0) + 1
        let userSegment = StorySegment(
            content: userMessage,
            role: .user,
            orderIndex: nextIndex,
            story: story
        )
        modelContext.insert(userSegment)
        story.updatedAt = Date()

        try? modelContext.save()

        await generateNextSegment(modelContext: modelContext)
    }

    var canRegenerate: Bool {
        guard let story = currentStory else { return false }
        let segments = story.orderedSegments
        return segments.last?.role == .assistant && segments.count > 1
    }

    var lastUserPrompt: String? {
        guard let story = currentStory else { return nil }
        let segments = story.orderedSegments
        return segments.last(where: { $0.role == .user })?.content
    }

    @MainActor
    func regenerateLastSegment(modelContext: ModelContext) async {
        guard let story = currentStory,
              let lastSegment = story.orderedSegments.last,
              lastSegment.role == .assistant else { return }

        modelContext.delete(lastSegment)
        story.updatedAt = Date()
        try? modelContext.save()

        await generateNextSegment(modelContext: modelContext)
    }

    @MainActor
    func regenerateWithEditedPrompt(_ newPrompt: String, modelContext: ModelContext) async {
        guard let story = currentStory else { return }

        let segments = story.orderedSegments

        // Find the last assistant segment and last user segment
        guard let lastAssistantIndex = segments.lastIndex(where: { $0.role == .assistant }),
              let lastUserIndex = segments.lastIndex(where: { $0.role == .user }),
              lastUserIndex < lastAssistantIndex else { return }

        let lastAssistant = segments[lastAssistantIndex]
        let lastUser = segments[lastUserIndex]

        // Delete the last assistant segment
        modelContext.delete(lastAssistant)

        // Update the last user prompt
        lastUser.content = newPrompt

        story.updatedAt = Date()
        try? modelContext.save()

        await generateNextSegment(modelContext: modelContext)
    }

    @MainActor
    private func generateNextSegment(modelContext: ModelContext) async {
        guard let story = currentStory else { return }

        isGenerating = true
        error = nil
        streamingContent = ""

        let messages = story.orderedSegments.map { segment in
            ChatMessage(role: segment.role.rawValue, content: segment.content)
        }

        do {
            let stream = await openRouter.streamChatCompletion(
                model: story.modelIdSnapshot,
                messages: messages,
                temperature: 0.8
            )

            var fullContent = ""

            for try await chunk in stream {
                fullContent += chunk
                streamingContent = fullContent
            }

            let nextIndex = (story.orderedSegments.last?.orderIndex ?? 0) + 1
            let assistantSegment = StorySegment(
                content: fullContent,
                role: .assistant,
                orderIndex: nextIndex,
                story: story
            )
            modelContext.insert(assistantSegment)
            story.updatedAt = Date()

            try? modelContext.save()

            streamingContent = ""

        } catch {
            self.error = error.localizedDescription
        }

        isGenerating = false
    }

    func loadExistingStory(_ story: Story) {
        currentStory = story
    }

    private func generateTitle(from prompt: String) -> String {
        let trimmed = prompt.trimmingCharacters(in: .whitespacesAndNewlines)
        if let periodIndex = trimmed.firstIndex(of: "."),
           trimmed.distance(from: trimmed.startIndex, to: periodIndex) < 60 {
            return String(trimmed[...periodIndex])
        }
        if trimmed.count <= 50 {
            return trimmed
        }
        let truncated = String(trimmed.prefix(47))
        return truncated + "..."
    }

    func deleteStory(_ story: Story, modelContext: ModelContext) {
        if currentStory?.id == story.id {
            currentStory = nil
        }
        modelContext.delete(story)
        try? modelContext.save()
    }
}
