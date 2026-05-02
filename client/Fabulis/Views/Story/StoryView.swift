import SwiftUI

struct StoryView: View {
    let storyId: Int
    let fallbackTitle: String

    @State private var detail: StoryDetail?
    @State private var errorMessage: String?
    @State private var isLoading = true

    var body: some View {
        Group {
            if let detail {
                List {
                    Section {
                        VStack(alignment: .leading, spacing: 6) {
                            Text(detail.title).font(.title2.bold())
                            Text(detail.categoryName).font(.subheadline).foregroundStyle(.secondary)
                            Text("\(detail.versions.count) \(detail.versions.count == 1 ? "version" : "versions")")
                                .font(.caption).foregroundStyle(.tertiary)
                        }
                    }
                    if detail.versions.isEmpty {
                        Text("No versions yet").font(.subheadline).foregroundStyle(.secondary)
                    } else {
                        Section("Versions") {
                            ForEach(detail.versions) { version in
                                NavigationLink(value: version) {
                                    VStack(alignment: .leading, spacing: 4) {
                                        Text("Version \(version.versionNumber)").font(.body.bold())
                                        Text(version.modelName).font(.caption).foregroundStyle(.secondary)
                                        Text(version.createdAt.formatted())
                                            .font(.caption2).foregroundStyle(.tertiary)
                                    }
                                }
                            }
                        }
                    }
                }
            } else if isLoading {
                ProgressView()
            } else if let errorMessage {
                VStack(spacing: 12) {
                    Text("Couldn't load story").font(.headline)
                    Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                    Button("Retry") { Task { await load() } }
                }
                .padding()
            }
        }
        .navigationTitle(detail?.title ?? fallbackTitle)
        .navigationDestination(for: StoryVersionSummary.self) { version in
            StoryVersionView(storyId: storyId, version: version.versionNumber, modelName: version.modelName)
        }
        .task { await load() }
        .refreshable { await load() }
    }

    private func load() async {
        do {
            errorMessage = nil
            detail = try await FabulisAPIClient.shared.story(id: storyId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }
}

extension StoryVersionSummary: Hashable {
    public func hash(into hasher: inout Hasher) { hasher.combine(id) }
    public static func == (lhs: StoryVersionSummary, rhs: StoryVersionSummary) -> Bool { lhs.id == rhs.id }
}
