# SPDX-License-Identifier: Apache-2.0
# Copyright (c) 2026 Jason Furr

require 'digest'
require 'json'

# Build adapter, not a second policy checker. Load FIX-13 unchanged and observe the visits it performs.
root, commit = ARGV
abort 'An exact lowercase build commit is required.' unless commit&.match?(/\A[0-9a-f]{40}(?:[0-9a-f]{24})?\z/)
parser = '.github/scripts/check-workflow-actions.rb'
load File.join(root, parser)

module CaptureWorkflowUses
  attr_accessor :uses

  def visit(node, file, allowed, errors)
    if node.is_a?(Psych::Nodes::Mapping)
      node.children.each_slice(2) do |key, value|
        next unless key.is_a?(Psych::Nodes::Scalar) && key.value == 'uses'
        uses << { path: ".github/workflows/#{file}", line: key.start_line + 1, action: value.value } if value.is_a?(Psych::Nodes::Scalar)
      end
    end
    super
  end
end
WorkflowActions.singleton_class.prepend(CaptureWorkflowUses)
WorkflowActions.uses = []
directory = File.join(root, '.github/workflows')
errors = WorkflowActions.check(directory)
abort errors.join("\n") unless errors.empty?
allow_path = '.github/workflows/actions.allow'
allowed = File.readlines(File.join(root, allow_path)).map { |line| value = line.split('#', 2).first.to_s.strip; value unless value.empty? }.compact
paths = [parser, allow_path] + Dir.glob(File.join(directory, '*.{yml,yaml}')).map { |p| ".github/workflows/#{File.basename(p)}" }
sources = paths.sort.map do |path|
  full = File.join(root, path)
  abort 'A workflow input must be a regular file.' unless File.file?(full) && !File.symlink?(full)
  { path: path, hash: "sha256:#{Digest::SHA256.file(full).hexdigest}" }
end
puts JSON.pretty_generate({ format: 'vericairn.workflow-inputs/1', commit: commit, sources: sources,
                            allowed: allowed.sort, uses: WorkflowActions.uses.sort_by { |u| [u[:path], u[:line]] } })
