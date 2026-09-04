import SwiftUI

extension View {
    /// Shows the model name beneath the navigation title.
    ///
    /// On iPhone we use the native `navigationSubtitle` (iOS 26+), which renders
    /// under the nav-bar title. On Mac Catalyst that modifier only feeds the window
    /// title bar, which isn't visible here, so we pin a subtitle bar under the nav bar.
    @ViewBuilder
    func modelSubtitle(_ subtitle: String?) -> some View {
        let model = (subtitle?.isEmpty == false) ? subtitle : nil
        #if targetEnvironment(macCatalyst)
        // Pin the model strip under the nav bar. The title must use inline display
        // mode here: a large title renders in the same top region and the pinned
        // bar would draw over it (you'd briefly see the title, then only the model).
        Group {
            if let model {
                safeAreaInset(edge: .top, spacing: 0) {
                    VStack(spacing: 0) {
                        Text(model)
                            .font(.caption)
                            .foregroundStyle(.secondary)
                            .frame(maxWidth: .infinity, alignment: .leading)
                            .padding(.horizontal)
                            .padding(.vertical, 6)
                        Divider()
                    }
                    .background(.bar)
                }
            } else {
                self
            }
        }
        .navigationBarTitleDisplayMode(.inline)
        #else
        if #available(iOS 26.0, *), let model {
            navigationSubtitle(model)
        } else {
            self
        }
        #endif
    }
}
