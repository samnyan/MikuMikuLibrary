#version 330 core

layout(location = 0) in vec2 aPosition;
layout(location = 1) in vec2 aTexCoord;

uniform mat4 uProjection;
uniform mat4 uTransform;
uniform vec4 uUvRect;

out vec2 vTexCoord;

void main()
{
    gl_Position = uProjection * uTransform * vec4(aPosition, 0.0, 1.0);
    vTexCoord = vec2(mix(uUvRect.x, uUvRect.z, aTexCoord.x),
                     mix(uUvRect.y, uUvRect.w, aTexCoord.y));
}
