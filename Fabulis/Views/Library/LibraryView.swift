import SwiftUI
import SwiftData

struct LibraryView: View {
    @Environment(\.modelContext) private var modelContext
    @Environment(LibraryViewModel.self) private var viewModel

    @Query(sort: \Storyteller.name) private var storytellers: [Storyteller]

    @State private var selectedStoryteller: Storyteller?
    @State private var showingEditor = false
    @State private var showingSettings = false

    var body: some View {
        NavigationStack {
            ScrollView {
                LazyVGrid(columns: [
                    GridItem(.flexible(), spacing: 16),
                    GridItem(.flexible(), spacing: 16)
                ], spacing: 16) {
                    ForEach(storytellers) { storyteller in
                        StorytellerCard(storyteller: storyteller)
                            .onTapGesture {
                                selectedStoryteller = storyteller
                            }
                    }
                }
                .padding()
            }
            .navigationTitle("Storytellers")
            .toolbar {
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        showingSettings = true
                    } label: {
                        Image(systemName: "gear")
                    }
                }
                ToolbarItem(placement: .primaryAction) {
                    Button {
                        showingEditor = true
                    } label: {
                        Image(systemName: "plus")
                    }
                }
            }
            .navigationDestination(item: $selectedStoryteller) { storyteller in
                StorytellerDetailView(storyteller: storyteller)
            }
            .sheet(isPresented: $showingEditor) {
                StorytellerEditorView(storyteller: nil)
            }
            .sheet(isPresented: $showingSettings) {
                SettingsView()
            }
            .task {
                await viewModel.loadModels()
            }
        }
    }
}

#Preview {
    LibraryView()
        .environment(LibraryViewModel())
        .modelContainer(for: Storyteller.self, inMemory: true)
}
