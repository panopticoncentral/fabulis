import Foundation
import SwiftData
import SwiftUI

@Observable
final class LibraryViewModel {
    var availableModels: [OpenRouterModel] = []
    var isLoadingModels: Bool = false
    var modelsError: String?

    private let openRouter = OpenRouterService.shared

    @MainActor
    func loadModels() async {
        guard availableModels.isEmpty else { return }

        isLoadingModels = true
        modelsError = nil

        do {
            availableModels = try await openRouter.fetchModels()
        } catch {
            modelsError = error.localizedDescription
        }

        isLoadingModels = false
    }

    func seedDefaultStorytellers(modelContext: ModelContext) {
        let descriptor = FetchDescriptor<Storyteller>()
        let existingCount = (try? modelContext.fetchCount(descriptor)) ?? 0

        guard existingCount == 0 else { return }

        for defaults in StorytellerDefaults.builtInStorytellers {
            let storyteller = Storyteller(
                name: defaults.name,
                subtitle: defaults.subtitle,
                iconName: defaults.iconName,
                accentColorHex: defaults.accentColorHex,
                systemPrompt: defaults.systemPrompt,
                selectedModelId: defaults.defaultModelId,
                isBuiltIn: true
            )
            modelContext.insert(storyteller)
        }

        try? modelContext.save()
    }

    func createCustomStoryteller(
        name: String,
        subtitle: String,
        iconName: String,
        accentColorHex: String,
        systemPrompt: String,
        modelId: String,
        modelContext: ModelContext
    ) -> Storyteller {
        let storyteller = Storyteller(
            name: name,
            subtitle: subtitle,
            iconName: iconName,
            accentColorHex: accentColorHex,
            systemPrompt: systemPrompt,
            selectedModelId: modelId,
            isBuiltIn: false
        )
        modelContext.insert(storyteller)
        try? modelContext.save()
        return storyteller
    }

    func deleteStoryteller(_ storyteller: Storyteller, modelContext: ModelContext) {
        modelContext.delete(storyteller)
        try? modelContext.save()
    }
}
