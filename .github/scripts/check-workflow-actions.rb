# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

require 'psych'
require 'set'

# Parse YAML's syntax tree, not lines: quoted keys, flow mappings, folded values and reusable jobs
# must receive the same check. Script bodies and comments are scalars, never workflow instructions.
module WorkflowActions
  PIN = /\A[A-Za-z0-9_.-]+\/[A-Za-z0-9_.-]+(?:\/[A-Za-z0-9_.-]+)*@[0-9a-fA-F]{40}\z/

  def self.check(directory)
    errors = []
    list = File.join(directory, 'actions.allow')
    return ['Missing actions.allow beside the workflows.'] unless File.file?(list)

    allowed = Set.new
    File.readlines(list).each_with_index do |line, index|
      action = line.split('#', 2).first.to_s.strip
      next if action.empty?
      if !PIN.match?(action) || !allowed.add?(action)
        errors << "actions.allow:#{index + 1}: invalid or duplicate full-SHA action: #{action}"
      end
    end
    files = Dir.glob(File.join(directory, '*.{yml,yaml}')).sort
    errors << 'No workflows found.' if files.empty?
    files.each do |path|
      begin
        visit(Psych.parse_stream(File.read(path)), File.basename(path), allowed, errors)
      rescue Psych::Exception => e
        errors << "#{File.basename(path)}: invalid YAML: #{e.message}"
      end
    end
    errors
  end

  def self.visit(node, file, allowed, errors)
    if node.is_a?(Psych::Nodes::Mapping)
      node.children.each_slice(2) do |key, value|
        next unless key.is_a?(Psych::Nodes::Scalar) && key.value == 'uses'
        action = value.is_a?(Psych::Nodes::Scalar) ? value.value : nil
        if action.nil? || !PIN.match?(action)
          errors << "#{file}:#{key.start_line + 1}: uses must name an action pinned to a full commit SHA."
        elsif !allowed.include?(action)
          errors << "#{file}:#{key.start_line + 1}: action is not in actions.allow: #{action}"
        end
      end
    end
    (node.children || []).each { |child| visit(child, file, allowed, errors) }
  end
end

if $PROGRAM_NAME == __FILE__
  errors = WorkflowActions.check(ARGV.fetch(0, File.expand_path('../workflows', __dir__)))
  abort errors.join("\n") unless errors.empty?
  puts 'Every workflow action is allowed and pinned to its declared full commit SHA.'
end
