import SwiftUI

/// A small sheet for editing a single trope: change its text and/or move it to
/// another category. Seeded from the summary already in the list, so it does
/// not need to fetch the trope itself — only the category list for the picker.
struct TropeEditSheet: View {
    let tropeId: Int
    /// Called after a successful save or delete so the presenter can reload.
    var onChanged: (() -> Void)? = nil

    @Environment(\.dismiss) private var dismiss
    @State private var text: String
    @State private var categoryId: Int
    @State private var categories: [CategorySummary] = []
    @State private var isLoading = true
    @State private var saving = false
    @State private var errorMessage: String?

    init(trope: TropeSummary, categoryId: Int, onChanged: (() -> Void)? = nil) {
        self.tropeId = trope.id
        self.onChanged = onChanged
        _text = State(initialValue: trope.text)
        _categoryId = State(initialValue: categoryId)
    }

    var body: some View {
        NavigationStack {
            Form {
                Section("Trope") {
                    TextField("Trope", text: $text, axis: .vertical)
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
                        Task { await delete() }
                    } label: {
                        Label("Delete Trope", systemImage: "trash")
                    }
                    .disabled(saving)
                }
            }
            .navigationTitle("Edit Trope")
            .toolbar {
                ToolbarItem(placement: .topBarLeading) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .topBarTrailing) {
                    Button {
                        Task { await save() }
                    } label: {
                        if saving { ProgressView().controlSize(.mini) } else { Text("Save") }
                    }
                    .disabled(saving || isLoading
                        || text.trimmingCharacters(in: .whitespacesAndNewlines).isEmpty)
                }
            }
            .overlay { if isLoading { ProgressView() } }
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
            _ = try await FabulisAPIClient.shared.updateTrope(
                id: tropeId, text: text, categoryId: categoryId)
            onChanged?()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }

    private func delete() async {
        saving = true; defer { saving = false }
        do {
            try await FabulisAPIClient.shared.deleteTrope(id: tropeId)
            onChanged?()
            dismiss()
        } catch {
            errorMessage = error.localizedDescription
        }
    }
}
