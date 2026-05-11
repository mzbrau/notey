---
name: Slate & Syntax
colors:
  surface: '#10131a'
  surface-dim: '#10131a'
  surface-bright: '#363941'
  surface-container-lowest: '#0b0e15'
  surface-container-low: '#191b23'
  surface-container: '#1d2027'
  surface-container-high: '#272a31'
  surface-container-highest: '#32353c'
  on-surface: '#e1e2ec'
  on-surface-variant: '#c2c6d6'
  inverse-surface: '#e1e2ec'
  inverse-on-surface: '#2e3038'
  outline: '#8c909f'
  outline-variant: '#424754'
  surface-tint: '#adc6ff'
  primary: '#adc6ff'
  on-primary: '#002e6a'
  primary-container: '#4d8eff'
  on-primary-container: '#00285d'
  inverse-primary: '#005ac2'
  secondary: '#b9c8de'
  on-secondary: '#233143'
  secondary-container: '#39485a'
  on-secondary-container: '#a7b6cc'
  tertiary: '#ffb786'
  on-tertiary: '#502400'
  tertiary-container: '#df7412'
  on-tertiary-container: '#461f00'
  error: '#ffb4ab'
  on-error: '#690005'
  error-container: '#93000a'
  on-error-container: '#ffdad6'
  primary-fixed: '#d8e2ff'
  primary-fixed-dim: '#adc6ff'
  on-primary-fixed: '#001a42'
  on-primary-fixed-variant: '#004395'
  secondary-fixed: '#d4e4fa'
  secondary-fixed-dim: '#b9c8de'
  on-secondary-fixed: '#0d1c2d'
  on-secondary-fixed-variant: '#39485a'
  tertiary-fixed: '#ffdcc6'
  tertiary-fixed-dim: '#ffb786'
  on-tertiary-fixed: '#311400'
  on-tertiary-fixed-variant: '#723600'
  background: '#10131a'
  on-background: '#e1e2ec'
  surface-variant: '#32353c'
typography:
  headline-lg:
    fontFamily: Inter
    fontSize: 32px
    fontWeight: '700'
    lineHeight: 40px
    letterSpacing: -0.02em
  headline-md:
    fontFamily: Inter
    fontSize: 24px
    fontWeight: '600'
    lineHeight: 32px
    letterSpacing: -0.01em
  headline-sm:
    fontFamily: Inter
    fontSize: 20px
    fontWeight: '600'
    lineHeight: 28px
  body-lg:
    fontFamily: Inter
    fontSize: 18px
    fontWeight: '400'
    lineHeight: 28px
  body-md:
    fontFamily: Inter
    fontSize: 16px
    fontWeight: '400'
    lineHeight: 24px
  code-inline:
    fontFamily: Inter
    fontSize: 14px
    fontWeight: '500'
    lineHeight: 20px
  label-sm:
    fontFamily: Inter
    fontSize: 12px
    fontWeight: '600'
    lineHeight: 16px
    letterSpacing: 0.05em
rounded:
  sm: 0.125rem
  DEFAULT: 0.25rem
  md: 0.375rem
  lg: 0.5rem
  xl: 0.75rem
  full: 9999px
spacing:
  unit: 4px
  container-max-width: 720px
  gutter: 24px
  margin-mobile: 16px
  margin-desktop: 48px
  stack-xs: 4px
  stack-sm: 8px
  stack-md: 16px
  stack-lg: 32px
---

## Brand & Style

This design system is built for focused writing and high-performance information management. It centers on the "Digital Sanctuary" concept—a distraction-free environment that prioritizes content over chrome. The personality is intellectual, precise, and utilitarian, borrowing from the efficiency of code editors while maintaining the elegance of modern typography.

The aesthetic follows a **Modern Minimalist Markdown** approach. It utilizes a deep, nocturnal color palette to reduce eye strain during long-form writing sessions. Visual hierarchy is established through extreme typographic clarity and purposeful use of vibrant accent colors rather than heavy decorative elements. The interface should feel like a sophisticated tool: sharp, responsive, and quiet.

## Colors

The color palette is anchored by a deep slate charcoal background, providing a low-light canvas that makes text appear to float. 

- **Core Neutrals:** The primary background uses a saturated charcoal (#0f172a). UI surfaces like sidebars or cards use a slightly lighter slate (#1e293b) to provide depth without breaking the dark-mode immersion.
- **Accents:** A vibrant blue (#3b82f6) is reserved for primary actions, focus states, and specific markdown syntax highlighting (like active headers or links).
- **Readability:** Primary text uses an off-white (#f8fafc) to maintain high contrast without the harshness of pure white (#ffffff) on a dark background. Secondary text and metadata use a muted slate gray (#94a3b8) to recede into the background.
- **Markdown Specifics:** Inline code and block quotes use a subtle surface tint to distinguish them from standard body text.

## Typography

This design system relies exclusively on **Inter** to maintain a systematic and neutral appearance. The typography system is designed for maximum legibility in a Markdown-centric workflow.

- **Markdown Headers:** Headlines use heavier weights (600-700) and negative letter-spacing to create a "locked-in" editorial feel. 
- **Body Text:** The body-lg role is the default for the note editor to ensure a comfortable reading experience. It uses a generous line height (1.5x+) to prevent text density fatigue.
- **Markdown Markers:** Symbols like `#`, `*`, and `>` should be rendered in a muted slate color (#475569) to remain visible but secondary to the content.
- **Scaling:** On mobile devices, headline-lg scales down to 24px (headline-md) to ensure titles do not wrap excessively.

## Layout & Spacing

The layout philosophy emphasizes a centered, single-column reading experience. 

- **The Editor:** For the main note-taking area, the design system employs a fixed-width container (720px max) centered on the screen. This mimics the line length of a physical page and improves horizontal scanning.
- **The Grid:** A strict 4px/8px baseline grid governs all vertical and horizontal rhythm. 
- **Navigation:** A collapsible sidebar (280px) sits to the left. On mobile, this becomes a full-screen overlay.
- **Margins:** Desktop views utilize wide gutters to create "breathing room," reinforcing the minimalist brand identity. Mobile views reduce these margins to 16px to maximize the narrow viewport.

## Elevation & Depth

This design system avoids traditional drop shadows to maintain a flat, modern minimalist aesthetic. Depth is instead communicated through **Tonal Layering** and **Low-Contrast Outlines**.

- **Level 0 (Base):** The deepest layer (#0f172a), used for the main editor background.
- **Level 1 (Surface):** Slightly lighter (#1e293b), used for sidebars, floating panels, and search bars.
- **Layer Separation:** Elements on Level 1 are separated from Level 0 using a 1px solid border (#334155).
- **Overlays:** Modals or tooltips use a more pronounced border (#475569) and a subtle 10% black tint behind them to dim the background content, rather than a blur.

## Shapes

The shape language is defined by the **ROUND_FOUR** principle, utilizing subtle 4px corner radii across all UI elements.

- **Buttons & Inputs:** Standardized at a 4px (0.25rem) radius. This provides a clean, professional look that is softer than sharp corners but more structured than fully rounded pills.
- **Cards & Modals:** Even larger containers maintain this 4px radius to ensure consistency and a "tight" architectural feel.
- **Selection States:** Hover states in lists (like the file explorer) use the same 4px radius for the background highlight.

## Components

### Buttons
- **Primary:** Solid Vibrant Blue (#3b82f6) with Bold White text. 4px rounded corners.
- **Secondary:** Transparent with a Slate border (#334155) and Off-White text.
- **Ghost:** No background or border; Blue text for actions, Slate text for navigation.

### Input Fields
- Background uses the Surface color (#1e293b) with a 1px border (#334155). 
- Active state changes the border color to Vibrant Blue (#3b82f6) with a subtle outer glow.

### Markdown Elements
- **Code Blocks:** Background #1e293b, 4px padding, and 1px border. 
- **Checkboxes:** Custom square icons with a 4px radius. When checked, the box fills with Vibrant Blue and displays a white checkmark.
- **Blockquotes:** A 4px solid blue vertical bar on the left edge with a slightly lighter slate text color.

### Navigation List
- Sidebar items have a 12px horizontal padding and 8px vertical padding. 
- Active items use a muted blue background tint (10% opacity) and a high-contrast white text color.

### Tooltips
- High-contrast: Slate 900 background with Slate 50 text. No shadow, 1px border.