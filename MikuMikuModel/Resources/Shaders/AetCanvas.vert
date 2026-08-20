#version 330 core

layout(location = 0) in vec2 aPosition;

uniform mat4 uProjection;
uniform mat4 uTransform;

void main()
{
    gl_Position = uProjection * uTransform * vec4(aPosition, 0.0, 1.0);
}
