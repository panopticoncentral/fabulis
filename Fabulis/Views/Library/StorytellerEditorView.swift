import SwiftUI
import SwiftData

struct StorytellerEditorView: View {
    @Environment(\.modelContext) private var modelContext
    @Environment(\.dismiss) private var dismiss
    @Environment(LibraryViewModel.self) private var libraryViewModel

    let storyteller: Storyteller?

    @State private var name: String = ""
    @State private var subtitle: String = ""
    @State private var iconName: String = "book.fill"
    @State private var accentColorHex: String = "#007AFF"
    @State private var systemPrompt: String = ""
    @State private var selectedModelId: String = "anthropic/claude-sonnet-4"

    @State private var showingModelPicker = false
    @State private var showingIconPicker = false

    private var isEditing: Bool { storyteller != nil }

    private let availableIcons = [
        "book.fill", "book.closed.fill", "books.vertical.fill",
        "text.book.closed.fill", "magazine.fill", "newspaper.fill",
        "scroll.fill", "doc.text.fill", "pencil.and.scribble",
        "theatermasks.fill", "sparkles", "wand.and.stars",
        "moon.stars.fill", "sun.max.fill", "flame.fill",
        "leaf.fill", "hare.fill", "pawprint.fill",
        "heart.fill", "star.fill", "bolt.fill",
        "magnifyingglass", "eye.fill", "brain.head.profile",
        "figure.walk", "airplane", "car.fill",
        "cup.and.saucer.fill", "fork.knife", "music.note"
    ]

    private let colorOptions = [
        "#007AFF", "#34C759", "#FF9500", "#FF3B30",
        "#AF52DE", "#5856D6", "#FF2D55", "#00C7BE",
        "#8B4513", "#2F4F4F", "#4169E1", "#DEB887",
        "#800020", "#FF6B6B", "#4A4A4A", "#228B22"
    ]

    var body: some View {
        NavigationStack {
            Form {
                Section("Identity") {
                    TextField("Name", text: $name)
                    TextField("Subtitle", text: $subtitle)

                    HStack {
                        Text("Icon")
                        Spacer()
                        Button {
                            showingIconPicker = true
                        } label: {
                            Image(systemName: iconName)
                                .font(.title2)
                        }
                    }

                    HStack {
                        Text("Color")
                        Spacer()
                        HStack(spacing: 8) {
                            ForEach(colorOptions.prefix(6), id: \.self) { hex in
                                Circle()
                                    .fill(Color(hex: hex) ?? .blue)
                                    .frame(width: 24, height: 24)
                                    .overlay(
                                        Circle()
                                            .stroke(Color.primary, lineWidth: hex == accentColorHex ? 2 : 0)
                                    )
                                    .onTapGesture {
                                        accentColorHex = hex
                                    }
                            }
                        }
                    }
                }

                Section("AI Model") {
                    Button {
                        showingModelPicker = true
                    } label: {
                        HStack {
                            Text("Model")
                                .foregroundStyle(.primary)
                            Spacer()
                            Text(selectedModelId)
                                .foregroundStyle(.secondary)
                                .lineLimit(1)
                            Image(systemName: "chevron.right")
                                .foregroundStyle(.tertiary)
                        }
                    }
                }

                Section("System Prompt") {
                    TextEditor(text: $systemPrompt)
                        .frame(minHeight: 200)
                }
            }
            .navigationTitle(isEditing ? "Edit Storyteller" : "New Storyteller")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
                ToolbarItem(placement: .confirmationAction) {
                    Button("Save") { save() }
                        .disabled(name.isEmpty || systemPrompt.isEmpty)
                }
            }
            .sheet(isPresented: $showingModelPicker) {
                ModelPickerView(selectedModelId: $selectedModelId)
            }
            .sheet(isPresented: $showingIconPicker) {
                IconPickerView(selectedIcon: $iconName, icons: availableIcons)
            }
            .onAppear {
                if let s = storyteller {
                    name = s.name
                    subtitle = s.subtitle
                    iconName = s.iconName
                    accentColorHex = s.accentColorHex
                    systemPrompt = s.systemPrompt
                    selectedModelId = s.selectedModelId
                }
            }
        }
    }

    private func save() {
        if let existing = storyteller {
            existing.name = name
            existing.subtitle = subtitle
            existing.iconName = iconName
            existing.accentColorHex = accentColorHex
            existing.systemPrompt = systemPrompt
            existing.selectedModelId = selectedModelId
            existing.updatedAt = Date()
        } else {
            _ = libraryViewModel.createCustomStoryteller(
                name: name,
                subtitle: subtitle,
                iconName: iconName,
                accentColorHex: accentColorHex,
                systemPrompt: systemPrompt,
                modelId: selectedModelId,
                modelContext: modelContext
            )
        }

        dismiss()
    }
}

struct IconPickerView: View {
    @Environment(\.dismiss) private var dismiss
    @Binding var selectedIcon: String
    let icons: [String]

    let columns = [GridItem(.adaptive(minimum: 50))]

    var body: some View {
        NavigationStack {
            ScrollView {
                LazyVGrid(columns: columns, spacing: 16) {
                    ForEach(icons, id: \.self) { icon in
                        Image(systemName: icon)
                            .font(.title)
                            .frame(width: 50, height: 50)
                            .background(selectedIcon == icon ? Color.accentColor.opacity(0.2) : Color.clear)
                            .clipShape(RoundedRectangle(cornerRadius: 8))
                            .onTapGesture {
                                selectedIcon = icon
                                dismiss()
                            }
                    }
                }
                .padding()
            }
            .navigationTitle("Choose Icon")
            .navigationBarTitleDisplayMode(.inline)
            .toolbar {
                ToolbarItem(placement: .cancellationAction) {
                    Button("Cancel") { dismiss() }
                }
            }
        }
    }
}

#Preview {
    StorytellerEditorView(storyteller: nil)
        .environment(LibraryViewModel())
        .modelContainer(for: Storyteller.self, inMemory: true)
}
