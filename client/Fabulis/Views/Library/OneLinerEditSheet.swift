import SwiftUI

/// A small sheet for editing a single one-liner: change its text and/or move it
/// to another category. Seeded from the summary already in the list, so it does
/// not need to fetch the one-liner itself — only the category list for the
/// picker.
struct OneLinerEditSheet: View {
    let oneLinerId: Int
    /// Called after a successful save or delete so the presenter can reload.
    var onChanged: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var text: String
    @State private var categoryId: Int
    @State private var categories: [CategorySummary] = []
    @State private var isLoading = true
    @State private var saving = false
    @State private var errorMessage: String?
    @State private var showingDeleteConfirm = false

    init(oneLiner: OneLinerSummary, categoryId: Int, onChanged: (() -> Void)? = nil) {
        self.oneLinerId = oneLiner.id
        self.onChanged = onChanged
        _text = State(initialValue: oneLiner.text)
        _categoryId = State(initialValue: categoryId)
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("One-liner") {
                    TextField("One-liner", text: $text, axis: .vertical)
                        .lineLimit(2...8)
                }
                Section("Category") {
                    Picker("Category", selection: $categoryId) {
                        ForEach(categories) { cat in
                            Text(cat.name).tag(cat.id)
                        }
                    }
                }
                Section {
                    Button(role: .destructive) {
                        showingDeleteConfirm = true
                    } label: {
                        Label("Delete One-liner", systemImage: "trash")
                    }
                    .disabled(saving)
                }
            }
            .navigationTitle("Edit One-liner")
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }.fixedSize()
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button {
                        Task { await save() }
                    } label: {
                        if saving { ProgressView().controlSize(.mini) } else { Text("Save") }
                    }
                    .disabled(saving || isLoading
                        || text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                    .fixedSize()
                }
            }
            .overlay { if isLoading { ProgressView() } }
            .alert("Delete one-liner?", isPresented: $showingDeleteConfirm) {
                Button("Cancel", role: .cancel) {}
                Button("Delete", role: .destructive) { Task { await delete() } }
            } message: {
                Text("This deletes the one-liner. This cannot be undone.")
            }
            .alert("Couldn't save", isPresented: Binding(
                get: { errorMessage != nil },
                set: { if !$0 { errorMessage = nil } })) {
                Button("OK", role: .cancel) {}
            } message: {
                Text(errorMessage ?? "")
            }
            .task { await load() }
        }
    }

    private func load() async {
        do {
            categories = try await FabulisAPIClient.shared.library().categories
        } catch {
            errorMessage = error.localizedDescription
        }
        isLoading = false
    }

    private func save() async {
        saving = true; defer { saving = false }
        do {
            _ = try await FabulisAPIClient.shared.updateOneLiner(
                id: oneLinerId, text: text, categoryId: categoryId)
            onChanged?()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func delete() async {
        saving = true; defer { saving = false }
        do {
            try await FabulisAPIClient.shared.deleteOneLiner(id: oneLinerId)
            onChanged?()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
