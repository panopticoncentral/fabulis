import SwiftUI

struct StorySummarySheet: View {
    let storyId: Int

    @Environment(\.dismiss) private var dismiss

    @State private var summary: StorySummaryDetail?
    @State private var isLoading = true
    @State private var errorMessage: String?
    @State private var isEditing = false
    @State private var editDraft: String = ""
    @State private var isSaving = false
    @State private var saveError: String?
    // True between requesting a regenerate and the server reflecting a new
    // summary. The background sweep keeps returning the OLD "ready" summary for
    // a moment, so we can't rely on the server status alone to show progress.
    @State private var awaitingRebuild = false
    @State private var pollTask: Task<Void, Never>?

    private var isBusy: Bool { awaitingRebuild || summary?.status == "generating" }

    var body: some View {
        NavigationStack {
            Group {
                if isLoading && summary == nil {
                    ProgressView()
                } else if isEditing {
                    editor
                } else {
                    content
                }
            }
            .padding()
            .frame(maxWidth: .infinity, maxHeight: .infinity, alignment: .top)
            .navigationTitle("Summary")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Done") { dismiss() }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    if isEditing {
                        Button("Save") { Task { await saveEdit() } }
                            .disabled(isSaving || editDraft.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    } else {
                        Menu {
                            Button("Edit", systemImage: "pencil") { beginEdit() }
                                .disabled(isBusy)
                            Button("Regenerate", systemImage: "arrow.clockwise") {
                                Task { await regenerate() }
                            }
                            .disabled(isBusy)
                        } label: {
                            Image(systemName: "ellipsis.circle")
                        }
                    }
                }
            }
        }
        .task { await load(); startPollingIfNeeded() }
        .onDisappear { pollTask?.cancel() }
    }

    @ViewBuilder
    private var content: some View {
        if isBusy {
            VStack(spacing: 12) {
                ProgressView()
                Text("Generating summary…").foregroundStyle(.secondary)
            }
        } else if let summary {
            switch summary.status {
            case "failed":
                VStack(alignment: .leading, spacing: 12) {
                    Label("Couldn't generate a summary", systemImage: "exclamationmark.triangle")
                        .foregroundStyle(.orange)
                    if let err = summary.error {
                        Text(err).font(.caption).foregroundStyle(.secondary)
                    }
                    Button("Try again") { Task { await regenerate() } }
                }
            case "ready":
                ScrollView {
                    VStack(alignment: .leading, spacing: 12) {
                        Text(summary.text ?? "").textSelection(.enabled)
                        if summary.isStale {
                            Text("A newer version exists — the summary will update shortly.")
                                .font(.caption).foregroundStyle(.secondary)
                        }
                    }
                }
            default: // "none"
                VStack(spacing: 12) {
                    Text("No summary yet.").foregroundStyle(.secondary)
                    Text("One will be generated automatically.")
                        .font(.caption).foregroundStyle(.secondary)
                }
            }
        } else if let errorMessage {
            VStack(spacing: 12) {
                Text("Couldn't load summary").font(.headline)
                Text(errorMessage).font(.caption).foregroundStyle(.secondary)
                Button("Retry") { Task { await load() } }
            }
        }
    }

    private var editor: some View {
        VStack(alignment: .leading, spacing: 8) {
            Text("Edit summary").font(.caption).foregroundStyle(.secondary)
            TextEditor(text: $editDraft)
                .frame(maxHeight: .infinity)
                .overlay(alignment: .topLeading) {
                    if isSaving { ProgressView().padding(6) }
                }
        }
        .alert("Couldn't save summary", isPresented: Binding(
            get: { saveError != nil },
            set: { if !$0 { saveError = nil } }
        )) {
            Button("OK", role: .cancel) { }
        } message: {
            if let saveError { Text(saveError) }
        }
    }

    private func load() async {
        isLoading = true
        do {
            summary = try await FabulisAPIClient.shared.storySummary(id: storyId)
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func beginEdit() {
        editDraft = summary?.text ?? ""
        saveError = nil
        isEditing = true
    }

    private func saveEdit() async {
        isSaving = true; defer { isSaving = false }
        do {
            summary = try await FabulisAPIClient.shared.updateStorySummary(
                id: storyId,
                text: editDraft.trimmingCharacters(in: .whitespacesAndNewlines))
            isEditing = false
        } catch {
            saveError = error.localizedDescription
        }
    }

    private func regenerate() async {
        let baseline = summary?.updatedAt
        do {
            try await FabulisAPIClient.shared.regenerateStorySummary(id: storyId)
            // The sweep keeps returning the old summary until it actually runs,
            // so show progress optimistically and poll until the summary changes.
            awaitingRebuild = true
            startPolling(awaitingChangeFrom: baseline)
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    /// Polls on first open only if the server is already mid-generation.
    private func startPollingIfNeeded() {
        guard summary?.status == "generating" else { return }
        startPolling(awaitingChangeFrom: summary?.updatedAt)
    }

    /// Re-fetches every few seconds (bounded) until the summary settles. A
    /// summary is settled once it is no longer generating AND either it failed
    /// or its `updatedAt` differs from `baseline` (so a stale "ready" returned
    /// before the rebuild runs doesn't end the wait early).
    private func startPolling(awaitingChangeFrom baseline: Date?) {
        pollTask?.cancel()
        let deadline = Date().addingTimeInterval(120)
        pollTask = Task {
            while !Task.isCancelled, Date() < deadline {
                try? await Task.sleep(for: .seconds(3))
                if Task.isCancelled { return }
                guard let latest = try? await FabulisAPIClient.shared.storySummary(id: storyId)
                else { continue }

                let settled = latest.status != "generating"
                    && (latest.status == "failed" || latest.updatedAt != baseline)
                if settled {
                    summary = latest
                    awaitingRebuild = false
                    return
                }
            }
            // Timed out — drop the optimistic state and show whatever we have.
            awaitingRebuild = false
        }
    }
}
