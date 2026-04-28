#version 330 core

in  vec2 vUV;
in  vec4 vColor;
out vec4 FragColor;

uniform sampler2D uFontAtlas;
uniform float     uScreenPxRange;  // = (fp_PxRange / fp_GlyphSize) * rendered_glyph_px_height
                                    // e.g. (4.0 / 48.0) * 14.0 = 1.166 for 14px text

float median(float r, float g, float b) {
    return max(min(r, g), min(max(r, g), b));
}

void main() {
    vec3  f_MSD = texture(uFontAtlas, vUV).rgb;
    float f_SD  = median(f_MSD.r, f_MSD.g, f_MSD.b);

    // screenPxDistance > 0 = inside glyph, < 0 = outside
    float f_ScreenPxDist = uScreenPxRange * (f_SD - 0.5);
    float f_Opacity      = clamp(f_ScreenPxDist + 0.5, 0.0, 1.0);

    FragColor = vec4(vColor.rgb, vColor.a * f_Opacity);
}