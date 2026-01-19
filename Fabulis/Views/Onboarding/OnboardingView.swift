import SwiftUI

struct OnboardingView: View {
    let onComplete: () -> Void

    @State private var viewModel = OnboardingViewModel()
    @FocusState private var isAPIKeyFocused: Bool

    var body: some View {
        NavigationStack {
            ScrollView {
                VStack(spacing: 32) {
                    VStack(spacing: 16) {
                        Image(systemName: "book.pages.fill")
                            .font(.system(size: 72))
                            .foregroundStyle(.tint)

                        Text("Welcome to Fabulis")
                            .font(.largeTitle.bold())

                        Text("Your personal storytelling companion powered by AI")
                            .font(.title3)
                            .foregroundStyle(.secondary)
                            .multilineTextAlignment(.center)
                    }
                    .padding(.top, 40)

                    VStack(alignment: .leading, spacing: 16) {
                        FeatureRow(
                            icon: "person.2.fill",
                            title: "Meet Your Storytellers",
                            description: "Choose from unique AI storytellers, each with their own style"
                        )
                        FeatureRow(
                            icon: "text.bubble.fill",
                            title: "Interactive Stories",
                            description: "Guide your story with prompts or let it unfold naturally"
                        )
                        FeatureRow(
                            icon: "books.vertical.fill",
                            title: "Your Library",
                            description: "Save and continue your stories anytime"
                        )
                    }
                    .padding(.horizontal)

                    Divider()
                        .padding(.horizontal)

                    VStack(alignment: .leading, spacing: 12) {
                        Text("Connect to OpenRouter")
                            .font(.headline)

                        Text("Fabulis uses OpenRouter to access AI models. Enter your API key to get started.")
                            .font(.subheadline)
                            .foregroundStyle(.secondary)

                        SecureField("sk-or-...", text: $viewModel.apiKey)
                            .textFieldStyle(.roundedBorder)
                            .textContentType(.password)
                            .autocorrectionDisabled()
                            .focused($isAPIKeyFocused)

                        if let error = viewModel.validationError {
                            Text(error)
                                .font(.caption)
                                .foregroundStyle(.red)
                        }

                        Link("Get your API key at openrouter.ai",
                             destination: URL(string: "https://openrouter.ai/keys")!)
                            .font(.caption)
                    }
                    .padding(.horizontal)

                    Button {
                        Task {
                            if await viewModel.validateAndSave() {
                                onComplete()
                            }
                        }
                    } label: {
                        Group {
                            if viewModel.isValidating {
                                ProgressView()
                            } else {
                                Text("Get Started")
                            }
                        }
                        .frame(maxWidth: .infinity)
                    }
                    .buttonStyle(.borderedProminent)
                    .controlSize(.large)
                    .disabled(!viewModel.canSubmit)
                    .padding(.horizontal)

                    Spacer(minLength: 40)
                }
            }
        }
    }
}

struct FeatureRow: View {
    let icon: String
    let title: String
    let description: String

    var body: some View {
        HStack(alignment: .top, spacing: 16) {
            Image(systemName: icon)
                .font(.title2)
                .foregroundStyle(.tint)
                .frame(width: 32)

            VStack(alignment: .leading, spacing: 4) {
                Text(title)
                    .font(.headline)
                Text(description)
                    .font(.subheadline)
                    .foregroundStyle(.secondary)
            }
        }
    }
}

#Preview {
    OnboardingView(onComplete: {})
}
