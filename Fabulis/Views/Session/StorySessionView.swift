import SwiftUI
import SwiftData

struct StorySessionView: View {
    @Environment(\.modelContext) private var modelContext

    @Bindable var story: Story
    @State var viewModel = StorySessionViewModel()
    @State private var userInput = ""
    @FocusState private var isInputFocused: Bool

    var body: some View {
        VStack(spacing: 0) {
            ScrollViewReader { proxy in
                ScrollView {
                    LazyVStack(alignment: .leading, spacing: 16) {
                        ForEach(viewModel.displaySegments) { segment in
                            StorySegmentView(segment: segment)
                                .id(segment.id)
                        }

                        if !viewModel.streamingContent.isEmpty {
                            StorySegmentView(
                                content: viewModel.streamingContent,
                                role: .assistant,
                                isStreaming: true
                            )
                            .id("streaming")
                        }

                        if let error = viewModel.error {
                            Text(error)
                                .foregroundStyle(.red)
                                .padding()
                                .background(Color.red.opacity(0.1))
                                .clipShape(RoundedRectangle(cornerRadius: 8))
                        }
                    }
                    .padding()
                }
                .onChange(of: viewModel.streamingContent) {
                    withAnimation {
                        proxy.scrollTo("streaming", anchor: .bottom)
                    }
                }
                .onChange(of: viewModel.displaySegments.count) {
                    if let lastId = viewModel.displaySegments.last?.id {
                        withAnimation {
                            proxy.scrollTo(lastId, anchor: .bottom)
                        }
                    }
                }
            }

            Divider()

            StoryInputView(
                userInput: $userInput,
                isGenerating: viewModel.isGenerating,
                canRegenerate: viewModel.canRegenerate,
                lastUserPrompt: viewModel.lastUserPrompt,
                onContinue: {
                    Task {
                        await viewModel.continueStory(modelContext: modelContext)
                    }
                },
                onSubmit: { direction in
                    Task {
                        await viewModel.continueStory(direction: direction, modelContext: modelContext)
                        userInput = ""
                    }
                },
                onRegenerate: {
                    Task {
                        await viewModel.regenerateLastSegment(modelContext: modelContext)
                    }
                },
                onRegenerateWithEdit: { newPrompt in
                    Task {
                        await viewModel.regenerateWithEditedPrompt(newPrompt, modelContext: modelContext)
                    }
                }
            )
            .focused($isInputFocused)
        }
        .navigationTitle(story.title)
        .navigationBarTitleDisplayMode(.inline)
        .toolbar {
            ToolbarItem(placement: .primaryAction) {
                Menu {
                    Button {
                        copyStoryToClipboard()
                    } label: {
                        Label("Copy Text", systemImage: "doc.on.doc")
                    }
                } label: {
                    Image(systemName: "ellipsis.circle")
                }
            }
        }
        .onAppear {
            viewModel.loadExistingStory(story)
        }
    }

    private func copyStoryToClipboard() {
        let storyText = viewModel.displaySegments
            .filter { $0.role == .assistant }
            .map { $0.content }
            .joined(separator: "\n\n")
        UIPasteboard.general.string = storyText
    }
}

struct NewStoryView: View {
    @Environment(\.modelContext) private var modelContext
    @Environment(\.dismiss) private var dismiss

    let storyteller: Storyteller

    @State private var viewModel = StorySessionViewModel()
    @State private var initialPrompt = ""
    @State private var hasStarted = false
    @FocusState private var isPromptFocused: Bool

    var body: some View {
        if let story = viewModel.currentStory {
            StorySessionView(story: story, viewModel: viewModel)
        } else if hasStarted {
            // Transitional state while story is being created
            ProgressView("Starting story...")
                .navigationTitle("New Story")
                .navigationBarTitleDisplayMode(.inline)
        } else {
            VStack(spacing: 24) {
                VStack(spacing: 8) {
                    Image(systemName: storyteller.iconName)
                        .font(.system(size: 48))
                        .foregroundStyle(Color(hex: storyteller.accentColorHex) ?? Color.accentColor)

                    Text(storyteller.name)
                        .font(.title2.bold())

                    Text(storyteller.subtitle)
                        .font(.subheadline)
                        .foregroundStyle(.secondary)
                }
                .padding(.top, 40)

                VStack(alignment: .leading, spacing: 8) {
                    Text("What story shall we tell?")
                        .font(.headline)

                    TextEditor(text: $initialPrompt)
                        .frame(minHeight: 120)
                        .padding(8)
                        .background(Color(.systemGray6))
                        .clipShape(RoundedRectangle(cornerRadius: 12))
                        .focused($isPromptFocused)

                    Text("Describe the setting, characters, or scenario you'd like to explore.")
                        .font(.caption)
                        .foregroundStyle(.secondary)
                }
                .padding(.horizontal)

                Spacer()

                Button {
                    hasStarted = true
                    Task {
                        await viewModel.startNewStory(
                            prompt: initialPrompt,
                            storyteller: storyteller,
                            modelContext: modelContext
                        )
                    }
                } label: {
                    Text("Begin Story")
                        .frame(maxWidth: .infinity)
                }
                .buttonStyle(.borderedProminent)
                .controlSize(.large)
                .disabled(initialPrompt.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                .padding(.horizontal)
                .padding(.bottom)
            }
            .navigationTitle("New Story")
            .navigationBarTitleDisplayMode(.inline)
            .onAppear {
                isPromptFocused = true
            }
        }
    }
}

#Preview {
    NewStoryView(storyteller: Storyteller(
        name: "The Bard",
        subtitle: "Epic fantasy adventures",
        iconName: "book.closed.fill",
        accentColorHex: "#8B4513",
        systemPrompt: "You are a storyteller",
        selectedModelId: "anthropic/claude-sonnet-4"
    ))
    .modelContainer(for: [Storyteller.self, Story.self, StorySegment.self], inMemory: true)
}
