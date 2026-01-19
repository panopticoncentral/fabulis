import SwiftUI
import SwiftData

struct StorytellerDetailView: View {
    @Environment(\.modelContext) private var modelContext
    @Environment(\.dismiss) private var dismiss
    @Environment(LibraryViewModel.self) private var viewModel

    let storyteller: Storyteller

    @State private var selectedStory: Story?
    @State private var showingNewStory = false
    @State private var showingEditor = false
    @State private var showingDeleteConfirmation = false

    private var sortedStories: [Story] {
        (storyteller.stories ?? []).sorted { $0.updatedAt > $1.updatedAt }
    }

    var body: some View {
        List {
            Section {
                Button {
                    showingNewStory = true
                } label: {
                    Label("New Story", systemImage: "plus.circle.fill")
                        .font(.headline)
                }
            }

            if !sortedStories.isEmpty {
                Section("Stories") {
                    ForEach(sortedStories) { story in
                        Button {
                            selectedStory = story
                        } label: {
                            StoryRowView(story: story)
                        }
                        .buttonStyle(.plain)
                    }
                    .onDelete(perform: deleteStories)
                }
            }
        }
        .navigationTitle(storyteller.name)
        .navigationBarTitleDisplayMode(.large)
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Menu {
                    Button {
                        showingEditor = true
                    } label: {
                        Label("Edit Storyteller", systemImage: "pencil")
                    }

                    Button(role: .destructive) {
                        showingDeleteConfirmation = true
                    } label: {
                        Label("Delete Storyteller", systemImage: "trash")
                    }
                } label: {
                    Image(systemName: "ellipsis.circle")
                }
            }
        }
        .sheet(isPresented: $showingEditor) {
            StorytellerEditorView(storyteller: storyteller)
        }
        .confirmationDialog(
            "Delete Storyteller",
            isPresented: $showingDeleteConfirmation,
            titleVisibility: .visible
        ) {
            Button("Delete", role: .destructive) {
                viewModel.deleteStoryteller(storyteller, modelContext: modelContext)
                dismiss()
            }
        } message: {
            Text("Are you sure you want to delete \"\(storyteller.name)\"? This will also delete all stories.")
        }
        .navigationDestination(item: $selectedStory) { story in
            StorySessionView(story: story)
        }
        .navigationDestination(isPresented: $showingNewStory) {
            NewStoryView(storyteller: storyteller)
        }
        .overlay {
            if sortedStories.isEmpty {
                ContentUnavailableView(
                    "No Stories Yet",
                    systemImage: "book.closed",
                    description: Text("Tap \"New Story\" to begin your first tale with \(storyteller.name)")
                )
                .offset(y: 60)
            }
        }
    }

    private func deleteStories(at offsets: IndexSet) {
        let storiesToDelete = offsets.map { sortedStories[$0] }
        for story in storiesToDelete {
            modelContext.delete(story)
        }
    }
}

struct StoryRowView: View {
    let story: Story

    var body: some View {
        VStack(alignment: .leading, spacing: 6) {
            Text(story.title)
                .font(.headline)
                .lineLimit(1)

            if let firstSegment = story.orderedSegments.first(where: { $0.role == .assistant }) {
                Text(firstSegment.content)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
                    .lineLimit(2)
            }

            Text(story.updatedAt, style: .relative)
                .font(.caption)
                .foregroundStyle(.tertiary)
        }
        .padding(.vertical, 4)
    }
}

#Preview {
    NavigationStack {
        StorytellerDetailView(storyteller: Storyteller(
            name: "The Bard",
            subtitle: "Epic fantasy adventures",
            iconName: "book.closed.fill",
            accentColorHex: "#8B4513",
            systemPrompt: "You are a storyteller",
            selectedModelId: "anthropic/claude-sonnet-4"
        ))
    }
    .modelContainer(for: [Storyteller.self, Story.self, StorySegment.self], inMemory: true)
}
